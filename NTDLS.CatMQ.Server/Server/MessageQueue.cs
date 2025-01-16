using NTDLS.CatMQ.Server.Management;
using NTDLS.CatMQ.Shared;
using NTDLS.Helpers;
using NTDLS.Semaphore;
using RocksDbSharp;
using System.Text.Json;

namespace NTDLS.CatMQ.Server.Server
{
    /// <summary>
    /// A named message queue and its delivery thread.
    /// </summary>
    internal class MessageQueue
    {
        private readonly Thread _deliveryThread;
        private readonly CMqServer _queueServer;

        internal AutoResetEvent DeliveryThreadWaitEvent = new(false);
        internal bool KeepRunning { get; set; } = false;

        /// <summary>
        /// List of subscriber connection IDs.
        /// </summary>
        internal OptimisticCriticalResource<Dictionary<Guid, CMqSubscriberDescriptor>> Subscribers { get; set; } = new();

        /// <summary>
        /// Messages that are enqueued in this list.
        /// </summary>
        internal OptimisticCriticalResource<EnqueuedMessageContainer> EnqueuedMessages { get; set; } = new();

        internal CMqQueueConfiguration Configuration { get; set; }

        internal MessageQueueStatistics Statistics { get; set; } = new();

        public MessageQueue(CMqServer mqServer, CMqQueueConfiguration queueConfiguration)
        {
            _queueServer = mqServer;
            Configuration = queueConfiguration;
            _deliveryThread = new(DeliveryThreadProc);
        }

        private void DeliveryThreadProc(object? p)
        {
#if DEBUG
            Thread.CurrentThread.Name = $"DeliveryThreadProc_{Environment.CurrentManagedThreadId}";
#endif
            var lastCheckpoint = DateTime.UtcNow;
            bool attemptBufferRehydration = false;

            while (KeepRunning)
            {
                var now = DateTime.UtcNow;

                if (DateTime.UtcNow - lastCheckpoint > TimeSpan.FromSeconds(30))
                {
                    if (Configuration.PersistenceScheme == CMqPersistenceScheme.Persistent)
                    {
                        EnqueuedMessages.TryWrite(1, o =>
                        {
                            if (o.Database != null)
                            {
                                //While the RockDB WAL logs data, it’s a good idea to flush the MemTable to disk periodically for additional safety.
                                _queueServer.InvokeOnLog(CMqErrorLevel.Verbose, $"Checkpoint persistent database for [{Configuration.QueueName}].");
                                o.Database.Flush(new FlushOptions());
                            }
                        });
                    }

                    lastCheckpoint = now;
                }

                bool shouldSleep = true; //Just used to omit waiting. We want to spin fast when we are delivering messages.

                try
                {
                    EnqueuedMessage? topMessage = null;
                    List<CMqSubscriberDescriptor>? yetToBeDeliveredSubscribers = null;

                    #region Get top message and its subscribers.

                    if (attemptBufferRehydration)
                    {
                        attemptBufferRehydration = false;
                        RehydrateMessageQueueFromDatabase();
                    }

                    EnqueuedMessages.TryReadAll([Subscribers], CMqDefaults.DEFAULT_TRY_WAIT_MS, m =>
                    {
                        Subscribers.Read(s =>
                        {
                            //We only process a queue if it has subscribers.
                            //This is so we do not discard messages as delivered for queues with no subscribers.
                            if (s.Count > 0)
                            {
                                //Get the first message in the list, if any.
                                topMessage = m.MessageBuffer.FirstOrDefault(o => o.DeferredUntil == null || now >= o.DeferredUntil);

                                if (Configuration.PersistenceScheme == CMqPersistenceScheme.Persistent)
                                {
                                    //We rehydrate the queue from the database when we have a queue-depth and either we didn't
                                    //  get a top-message or we are under the minimum buffer size. We have to check for the NULL
                                    //  top-message because it could be that we do not have any qualified messages in the buffer
                                    //  (because all of them are deferred) even though we do have messages in the buffer.
                                    if (Statistics.QueueDepth > 0 && (topMessage == null || m.MessageBuffer.Count < CMqDefaults.DEFAULT_PERSISTENT_MESSAGES_MIN_BUFFER))
                                    {
                                        //If we have more items in the queue than we have in the buffer, then trigger a rehydrate.
                                        attemptBufferRehydration = (Statistics.QueueDepth > m.MessageBuffer.Count);
                                    }
                                }

                                if (topMessage != null)
                                {
                                    //Get list of subscribers that have yet to get a copy of the message.

                                    var yetToBeDeliveredSubscriberIds = s.Keys.Except(topMessage.SatisfiedSubscribersSubscriberIDs).ToList();

                                    yetToBeDeliveredSubscribers = s.Where(o => yetToBeDeliveredSubscriberIds.Contains(o.Key)).Select(o => o.Value).ToList();

                                    if (Configuration.DeliveryScheme == CMqDeliveryScheme.Balanced)
                                    {
                                        yetToBeDeliveredSubscribers = yetToBeDeliveredSubscribers.OrderBy(_ => Guid.NewGuid()).ToList();
                                    }
                                }
                            }
                        });

                        if (Configuration.MaxMessageAge > TimeSpan.Zero)
                        {
                            //Get the first message in the list, if any.
                            //We do this so that we can test for expired messages even when no subscribers are present.
                            var testExpired = topMessage ?? m.MessageBuffer.FirstOrDefault();

                            if (testExpired != null)
                            {
                                if ((DateTime.UtcNow - testExpired.Timestamp) > Configuration.MaxMessageAge)
                                {
                                    //If MaxMessageAge is defined, then remove the stale messages.

                                    if (Configuration.DeadLetterConfiguration != null)
                                    {
                                        if ((DateTime.UtcNow - testExpired.Timestamp) > Configuration.DeadLetterConfiguration.MaxMessageAge)
                                        {
                                            //Even too old for the dead-letter queue, discard expired message.
                                        }
                                        else
                                        {
                                            _queueServer.ShovelToDeadLetter(Configuration.QueueName, testExpired);
                                        }
                                    }

                                    m.Database?.Remove(testExpired.SerialNumber.ToString());
                                    m.MessageBuffer.Remove(testExpired);
                                    Statistics.DecrementQueueDepth();
                                    Statistics.ExpiredMessageCount++;

                                    topMessage = null; //Make sure we do not process the topMessage (if any).
                                    shouldSleep = false;
                                }
                            }
                        }
                    });

                    #endregion

                    //We we have a message, deliver it to the queue subscribers.
                    if (topMessage != null && yetToBeDeliveredSubscribers != null)
                    {
                        bool deliveredAndConsumed = false;
                        bool deadLetterRequested = false;
                        bool dropMessageRequested = false;

                        #region Deliver message to subscibers.

                        foreach (var subscriber in yetToBeDeliveredSubscribers)
                        {
                            if (KeepRunning == false)
                            {
                                break;
                            }

                            //Keep track of per-message-subscriber delivery metrics.
                            if (topMessage.SubscriberMessageDeliveries.TryGetValue(subscriber.SubscriberId, out var subscriberMessageDelivery))
                            {
                                subscriberMessageDelivery.DeliveryAttemptCount++;
                            }
                            else
                            {
                                subscriberMessageDelivery = new SubscriberMessageDelivery() { DeliveryAttemptCount = 1 };
                                topMessage.SubscriberMessageDeliveries.Add(subscriber.SubscriberId, subscriberMessageDelivery);
                            }

                            try
                            {
                                subscriber.AttemptedDeliveryCount++;

                                var consumeResult = _queueServer.DeliverMessage(subscriber.SubscriberId, Configuration.QueueName, topMessage);

                                Statistics.DeliveredMessageCount++;
                                subscriber.SuccessfulDeliveryCount++;

                                if (consumeResult.Disposition == CMqConsumptionDisposition.Consumed)
                                {
                                    subscriber.ConsumedDeliveryCount++;
                                    deliveredAndConsumed = true;

                                    //The message was marked as consumed by the subscriber, so we are done with this subscriber.

                                    //This thread is the only place we manage [SatisfiedSubscribersConnectionIDs], so we can use it without additional locking.
                                    topMessage.SatisfiedSubscribersSubscriberIDs.Add(subscriber.SubscriberId);
                                    shouldSleep = false;

                                    if (Configuration.ConsumptionScheme == CMqConsumptionScheme.FirstConsumedSubscriber)
                                    {
                                        //Message was delivered and consumed, break the delivery loop so the message can be removed from the queue.
                                        break;
                                    }
                                }
                                else if (consumeResult.Disposition == CMqConsumptionDisposition.Defer)
                                {
                                    //the message was marked as deferred by the subscriber, we will retry this subscriber at a later time.
                                    topMessage.DeferredUntil = now + consumeResult.DeferDuration;
                                    subscriber.DeferredDeliveryCount++;
                                    Statistics.DeferredDeliveryCount++;

                                    if (Configuration.ConsumptionScheme == CMqConsumptionScheme.FirstConsumedSubscriber)
                                    {
                                        //Message was delivered and consumed, break the delivery loop so the message can be removed from the queue.
                                        break;
                                    }
                                }
                                else if (consumeResult.Disposition == CMqConsumptionDisposition.NotConsumed)
                                {
                                    //The message was marked as not-consumed by the subscriber, so we are done with this subscriber.
                                    topMessage.SatisfiedSubscribersSubscriberIDs.Add(subscriber.SubscriberId);
                                    shouldSleep = false;
                                }
                                else if (consumeResult.Disposition == CMqConsumptionDisposition.DeadLetter)
                                {
                                    Statistics.ExplicitDeadLetterCount++;
                                    deadLetterRequested = true;
                                    shouldSleep = false;
                                    break;
                                }
                                else if (consumeResult.Disposition == CMqConsumptionDisposition.Drop)
                                {
                                    Statistics.ExplicitDropCount++;
                                    dropMessageRequested = true;
                                    shouldSleep = false;
                                    break;
                                }

                                if (KeepRunning == false)
                                {
                                    break;
                                }
                            }
                            catch (Exception ex) //Delivery failure.
                            {
                                Statistics.ExpiredMessageCount++;
                                subscriber.FailedDeliveryCount++;
                                _queueServer.InvokeOnLog(ex.GetBaseException());
                            }

                            //If we have tried to deliver this message to this subscriber too many times, then mark this subscriber-message as satisfied.
                            if (Configuration.MaxDeliveryAttempts > 0
                                && subscriberMessageDelivery.DeliveryAttemptCount >= Configuration.MaxDeliveryAttempts)
                            {
                                topMessage.FailedSubscribersSubscriberIDs.Add(subscriber.SubscriberId);
                                topMessage.SatisfiedSubscribersSubscriberIDs.Add(subscriber.SubscriberId);
                            }
                        }

                        #endregion

                        #region Delivery Throttle.

                        if (Configuration.DeliveryThrottle > TimeSpan.Zero)
                        {
                            if (Configuration.DeliveryThrottle.TotalSeconds >= 1)
                            {
                                int sleepSeconds = (int)Configuration.DeliveryThrottle.TotalSeconds;
                                for (int sleep = 0; sleep < sleepSeconds && KeepRunning; sleep++)
                                {
                                    Thread.Sleep(1000);
                                }
                            }
                            else
                            {
                                Thread.Sleep((int)Configuration.DeliveryThrottle.TotalMilliseconds);
                            }
                        }

                        #endregion

                        #region Remove message from queue.

                        bool removeSuccess;

                        do
                        {
                            removeSuccess = true;

                            EnqueuedMessages.Write(m =>
                            {
                                removeSuccess = Subscribers.TryRead(CMqDefaults.DEFAULT_TRY_WAIT_MS, s =>
                                {
                                    if (dropMessageRequested)
                                    {
                                        //A subscriber requested that the message be dropped, so remove the message from the queue and cache.
                                        m.Database?.Remove(topMessage.SerialNumber.ToString());
                                        m.MessageBuffer.Remove(topMessage);
                                        Statistics.DecrementQueueDepth();
                                    }
                                    else if (deadLetterRequested)
                                    {
                                        //A subscriber requested that we dead-letter this message.

                                        if (Configuration.DeadLetterConfiguration != null)
                                        {
                                            if ((DateTime.UtcNow - topMessage.Timestamp) > Configuration.DeadLetterConfiguration.MaxMessageAge)
                                            {
                                                //Message is even too old for the dead-letter queue, discard expired message.
                                            }
                                            else
                                            {
                                                _queueServer.ShovelToDeadLetter(Configuration.QueueName, topMessage);
                                            }
                                        }

                                        //Remove the message from the queue and cache.
                                        m.Database?.Remove(topMessage.SerialNumber.ToString());
                                        m.MessageBuffer.Remove(topMessage);
                                        Statistics.DecrementQueueDepth();
                                    }
                                    else if (Configuration.ConsumptionScheme == CMqConsumptionScheme.FirstConsumedSubscriber)
                                    {
                                        //If the queue uses the FirstConsumedSubscriber schema and the message was successfully consumed
                                        //  by a subscriber, then remove the message from the queue.
                                        if (deliveredAndConsumed)
                                        {
                                            //The message was consumed by a subscriber, remove it from the queue and cache.
                                            m.Database?.Remove(topMessage.SerialNumber.ToString());
                                            m.MessageBuffer.Remove(topMessage);
                                            Statistics.DecrementQueueDepth();
                                        }
                                    }
                                    else if (s.Keys.Except(topMessage.SatisfiedSubscribersSubscriberIDs).Any() == false)
                                    {
                                        //All subscribers have received a copy of the message or have received their maximum number
                                        //  of retries, so we can now remove the message from the queue.

                                        //If there were any failed deliveries, then we want to retain a copy of the message in the DLQ.
                                        if (topMessage.FailedSubscribersSubscriberIDs.Count != 0)
                                        {
                                            if (Configuration.DeadLetterConfiguration != null)
                                            {
                                                if ((DateTime.UtcNow - topMessage.Timestamp) > Configuration.DeadLetterConfiguration.MaxMessageAge)
                                                {
                                                    //Message is even too old for the dead-letter queue, discard expired message.
                                                }
                                                else
                                                {
                                                    _queueServer.ShovelToDeadLetter(Configuration.QueueName, topMessage);
                                                }
                                            }
                                        }

                                        //Remove the message from the queue and cache.
                                        m.Database?.Remove(topMessage.SerialNumber.ToString());
                                        m.MessageBuffer.Remove(topMessage);
                                        Statistics.DecrementQueueDepth();
                                    }
                                }) && removeSuccess;
                            });

                            if (!removeSuccess)
                            {
                                Thread.Sleep(CMqDefaults.DEFAULT_DEADLOCK_AVOIDANCE_WAIT_MS);
                            }
                        } while (KeepRunning && removeSuccess == false);

                        #endregion

                        if (KeepRunning == false)
                        {
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _queueServer.InvokeOnLog(ex.GetBaseException());
                }

                if (shouldSleep)
                {
                    //If nothing was successfully delivered, then delay for a period.
                    DeliveryThreadWaitEvent.WaitOne(10);
                }
            }
        }

        private bool RehydrateMessageQueueFromDatabase()
        {
            if (Configuration.PersistenceScheme != CMqPersistenceScheme.Persistent)
            {
                return false;
            }

            _queueServer.InvokeOnLog(CMqErrorLevel.Verbose, $"Re-hydrating message buffer for [{Configuration.QueueName}].");

            return EnqueuedMessages.TryWrite(m =>
            {
                if (m.Database == null)
                {
                    return;
                }

                ulong? maxSerialNumber = null;

                if (m.MessageBuffer.Count > 0)
                {
                    maxSerialNumber = ulong.Parse(m.MessageBuffer.Max(o => o.SerialNumber).EnsureNotNull());
                }

                int messagesLoaded = 0;

                using var iterator = m.Database.NewIterator();
                for (iterator.SeekToFirst(); iterator.Valid() && messagesLoaded < CMqDefaults.DEFAULT_PERSISTENT_MESSAGES_BUFFER_SIZE; iterator.Next())
                {
                    var persistedMessage = JsonSerializer.Deserialize<EnqueuedMessage>(iterator.StringValue());
                    if (persistedMessage != null)
                    {
                        if (maxSerialNumber == null || ulong.Parse(persistedMessage.SerialNumber) > maxSerialNumber)
                        {
                            m.MessageBuffer.Add(persistedMessage);
                            messagesLoaded++;
                        }
                    }
                }
            });
        }

        public void InitializePersistentDatabase()
        {
            if (Configuration.PersistenceScheme != CMqPersistenceScheme.Persistent)
            {
                return;
            }

            var deadLetterSerialNumbers = new HashSet<string>();

            _queueServer.InvokeOnLog(CMqErrorLevel.Information, $"Creating persistent path for [{Configuration.QueueName}].");

            var databasePath = Path.Join(_queueServer.Configuration.PersistencePath, "messages", Configuration.QueueName);
            Directory.CreateDirectory(databasePath);

            _queueServer.InvokeOnLog(CMqErrorLevel.Information, $"Instantiating persistent database for [{Configuration.QueueName}].");
            var options = new DbOptions().SetCreateIfMissing(true);
            var persistenceDatabase = RocksDb.Open(options, databasePath);

            _queueServer.InvokeOnLog(CMqErrorLevel.Information, $"Loading persistent messages for [{Configuration.QueueName}].");
            using var iterator = persistenceDatabase.NewIterator();

            ulong maxSerialNumber = 0;

            EnqueuedMessages.Write(m =>
            {
                Statistics.SetQueueDepth(0);

                for (iterator.SeekToFirst(); iterator.Valid(); iterator.Next())
                {
                    var persistedMessage = JsonSerializer.Deserialize<EnqueuedMessage>(iterator.StringValue());
                    if (persistedMessage != null)
                    {
                        var serialNumber = ulong.Parse(persistedMessage.SerialNumber);
                        if (serialNumber > maxSerialNumber)
                        {
                            //Keep track of the max serial number so we know where to start from with the next message.
                            maxSerialNumber = serialNumber;
                        }

                        //If we have a MaxMessageAge, check it and if the message is expired, either
                        //  dead-letter or discard it instead of adding it to its original queue.
                        if (Configuration.MaxMessageAge > TimeSpan.Zero)
                        {
                            if ((DateTime.UtcNow - persistedMessage.Timestamp) > Configuration.MaxMessageAge)
                            {
                                if (Configuration.DeadLetterConfiguration != null)
                                {
                                    if ((DateTime.UtcNow - persistedMessage.Timestamp) > Configuration.DeadLetterConfiguration.MaxMessageAge)
                                    {
                                        //Message is even too old for the dead-letter queue, discard expired message.
                                    }
                                    else
                                    {
                                        Statistics.ExpiredMessageCount++;
                                        _queueServer.ShovelToDeadLetter(Configuration.QueueName, persistedMessage);
                                        deadLetterSerialNumbers.Add(persistedMessage.SerialNumber);
                                    }
                                }
                                else
                                {
                                    //No dead-letter queue, discard expired message.
                                    deadLetterSerialNumbers.Add(persistedMessage.SerialNumber);
                                }
                            }
                            else
                            {
                                //For persistent queues, the messages are only loaded into the database.
                                //They will be buffered into the message buffer by the message queue delivery thread.
                                Statistics.IncrementQueueDepth();
                            }
                        }
                        else
                        {
                            //For persistent queues, the messages are only loaded into the database.
                            //They will be buffered into the message buffer by the message queue delivery thread.
                            Statistics.IncrementQueueDepth();
                        }
                    }
                }

                if (deadLetterSerialNumbers.Count > 0)
                {
                    _queueServer.InvokeOnLog(CMqErrorLevel.Information, $"Removing {deadLetterSerialNumbers.Count:n0} dead-lettered messages from [{Configuration.QueueName}].");
                    foreach (var deadLetterSerialNumber in deadLetterSerialNumbers)
                    {
                        persistenceDatabase.Remove(deadLetterSerialNumber);
                    }
                }

                m.Database = persistenceDatabase;
            });

            Statistics.SetNextSerialNumber(maxSerialNumber + 1);
        }

        public void StartAsync()
        {
            _queueServer.InvokeOnLog(CMqErrorLevel.Information, $"Starting delivery thread for [{Configuration.QueueName}].");
            KeepRunning = true;
            _deliveryThread.Start();
        }

        public void StopAsync()
        {
            _queueServer.InvokeOnLog(CMqErrorLevel.Information, $"Signaling shutdown for [{Configuration.QueueName}].");
            KeepRunning = false;
        }

        public void WaitOnStop()
        {
            _queueServer.InvokeOnLog(CMqErrorLevel.Information, $"Waiting on delivery thread to quit for [{Configuration.QueueName}].");
            _deliveryThread.Join();

            _queueServer.InvokeOnLog(CMqErrorLevel.Information, $"Shutting down database connection for [{Configuration.QueueName}].");
            EnqueuedMessages.Write(m =>
            {
                if (m.Database != null)
                {
                    m.Database?.Dispose();
                    m.Database = null;
                }
            });
        }
    }
}
