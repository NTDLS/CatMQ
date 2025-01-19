using NTDLS.CatMQ.Server.Management;
using NTDLS.CatMQ.Shared;
using NTDLS.Semaphore;
using RocksDbSharp;
using System.Text;
using System.Text.Json;

namespace NTDLS.CatMQ.Server.Server
{
    /// <summary>
    /// A named message queue and its delivery thread.
    /// </summary>
    internal class SingleMessageQueueServer
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

        public SingleMessageQueueServer(CMqServer mqServer, CMqQueueConfiguration queueConfiguration)
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

            int outstandingDeliveries = 0;

            while (KeepRunning)
            {
                var now = DateTime.UtcNow;
                bool yieldThread = true; //Just used to omit waiting. We want to spin fast when we are delivering messages.

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

                try
                {
                    EnqueuedMessage? topMessage = null;
                    var allSubscriberIDs = new HashSet<Guid>();

                    #region Get top message and its subscribers.

                    if (attemptBufferRehydration)
                    {
                        attemptBufferRehydration = false;
                        HydrateMessageBuffer(CMqDefaults.DEFAULT_PERSISTENT_MESSAGES_BUFFER_SIZE);
                    }

                    EnqueuedMessages.TryReadAll([Subscribers], CMqDefaults.DEFAULT_TRY_WAIT_MS, m =>
                    {
                        if (Configuration.MaxMessageAge != null)
                        {
                            //Look for and flag expired messages.
                            var expiredMessages = m.MessageBuffer.Where(o =>
                                o.State == CMqMessageState.Ready
                                && (now - o.Timestamp) > Configuration.MaxMessageAge);

                            foreach (var message in expiredMessages)
                            {
                                //If MaxMessageAge is defined, then remove the stale messages.
                                if (Configuration.DeadLetterConfiguration != null)
                                {
                                    if ((now - message.Timestamp) > Configuration.DeadLetterConfiguration.MaxMessageAge)
                                    {
                                        //Even too old for the dead-letter queue, discard expired message.
                                        message.State = CMqMessageState.Drop;
                                    }
                                    else
                                    {
                                        message.State = CMqMessageState.DeadLetter;
                                    }
                                }
                            }
                        }

                        Subscribers.Read(s => //This lock is already held.
                        {
                            //We only process a queue if it has subscribers.
                            //This is so we do not discard messages as delivered for queues with no subscribers.
                            if (s.Count > 0)
                            {
                                //Get the first message in the list, if any.
                                topMessage = m.MessageBuffer.FirstOrDefault(o =>
                                    o.State == CMqMessageState.Ready
                                    && (o.DeferredUntil == null || now >= o.DeferredUntil));

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
                                    allSubscriberIDs = s.Keys.ToHashSet();
                                }
                            }
                        });
                    });

                    #endregion

                    /*   
                     *                                                   _queueServer.ShovelToDeadLetter(Configuration.QueueName, testExpired);

                     *   m.RemoveFromBufferAndDatabase(testExpired);
                        Statistics.DecrementQueueDepth();
                        Statistics.ExpiredMessageCount++;

                        topMessage = null; //Make sure we do not process the topMessage (if any).*/


                    //We we have a message, deliver it to the queue subscribers.
                    if (topMessage != null)
                    {
                        //TODO async deliver message.

                        DistributeToSubscribers(topMessage);

                        yieldThread = false;

                        bool deliveredAndConsumed = false;
                        bool deadLetterRequested = false;
                        bool dropMessageRequested = false;



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
                                        m.RemoveFromBufferAndDatabase(topMessage);
                                        Statistics.DecrementQueueDepth();
                                    }
                                    else if (deadLetterRequested)
                                    {
                                        //A subscriber requested that we dead-letter this message.

                                        if (Configuration.DeadLetterConfiguration != null)
                                        {
                                            if (Configuration.DeadLetterConfiguration.MaxMessageAge != null
                                                && (DateTime.UtcNow - topMessage.Timestamp) > Configuration.DeadLetterConfiguration.MaxMessageAge)
                                            {
                                                //Message is even too old for the dead-letter queue, discard expired message.
                                            }
                                            else
                                            {
                                                _queueServer.ShovelToDeadLetter(Configuration.QueueName, topMessage);
                                            }
                                        }

                                        //Remove the message from the queue and cache.
                                        m.RemoveFromBufferAndDatabase(topMessage);
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

                if (yieldThread && KeepRunning)
                {
                    //If nothing was successfully delivered, then delay for a period.
                    DeliveryThreadWaitEvent.WaitOne(10);
                }
            }
        }

        private async Task DistributeToSubscribers(EnqueuedMessage message)
        {
            HashSet<Guid>? allSubscriberIDs = null;
            HashSet<Guid>? remainingSubscriberIds = null;
            List<CMqSubscriberDescriptor>? remainingSubscribers = null;

            Subscribers.Read(s =>
            {
                allSubscriberIDs = s.Keys.ToHashSet();
                remainingSubscriberIds = s.Keys.Except(message.SatisfiedSubscriberIDs).ToHashSet();
                remainingSubscribers = s.Where(o => remainingSubscriberIds.Contains(o.Key)).Select(o => o.Value).ToList();
            });

            if (!(remainingSubscribers?.Count > 0) || !(remainingSubscriberIds?.Count > 0) || !(remainingSubscribers?.Count > 0))
            {
                //The flow control below should not allow us to ever get here, but logically I feel we need to test for it.
                //Given that this is an "exception" to the proper flow, I think the only appropriate action to to give a
                //warning and dead-letter the message.
                message.State = CMqMessageState.DeadLetter;
                return;
            }

            if (Configuration.DeliveryScheme == CMqDeliveryScheme.Balanced)
            {
                remainingSubscribers = remainingSubscribers.OrderBy(_ => Guid.NewGuid()).ToList();
            }

            foreach (var subscriber in remainingSubscribers)
            {
                if (KeepRunning == false)
                {
                    return;
                }

                //Keep track of per-message-subscriber delivery metrics.
                if (message.SubscriberMessageDeliveries.TryGetValue(subscriber.SubscriberId, out var subscriberMessageDelivery))
                {
                    subscriberMessageDelivery.DeliveryAttemptCount++;
                }
                else
                {
                    subscriberMessageDelivery = new SubscriberMessageDelivery() { DeliveryAttemptCount = 1 };
                    message.SubscriberMessageDeliveries.Add(subscriber.SubscriberId, subscriberMessageDelivery);
                }

                try
                {
                    subscriber.AttemptedDeliveryCount++;

                    var consumeResult = await _queueServer.DeliverMessage(subscriber.SubscriberId, Configuration.QueueName, message);

                    Statistics.DeliveredMessageCount++;
                    subscriber.SuccessfulDeliveryCount++;

                    if (consumeResult.Disposition == CMqConsumptionDisposition.Consumed)
                    {
                        subscriber.ConsumedDeliveryCount++;
                        message.State = CMqMessageState.Drop;

                        //The message was marked as consumed by the subscriber, so we are done with this subscriber.
                        message.SatisfiedSubscriberIDs.Add(subscriber.SubscriberId);

                        if (Configuration.ConsumptionScheme == CMqConsumptionScheme.FirstConsumedSubscriber)
                        {
                            //Message was delivered and consumed, break the delivery loop so the message can be removed from the queue.
                            message.State = CMqMessageState.Drop;
                            break;
                        }
                    }
                    else if (consumeResult.Disposition == CMqConsumptionDisposition.Defer)
                    {
                        //the message was marked as deferred by the subscriber, we will retry this subscriber at a later time.
                        message.DeferredUntil = DateTime.UtcNow + consumeResult.DeferDuration;
                        message.DeferDuration = consumeResult.DeferDuration;
                        message.DeferredCount++;
                        subscriber.DeferredDeliveryCount++;
                        Statistics.DeferredDeliveryCount++;

                        EnqueuedMessages.Read(m => m.Database.Store(message));

                        message.State = CMqMessageState.Ready;
                    }
                    else if (consumeResult.Disposition == CMqConsumptionDisposition.NotConsumed)
                    {
                        //The message was marked as not-consumed by the subscriber, so we are done with this subscriber.
                        message.SatisfiedSubscriberIDs.Add(subscriber.SubscriberId);
                    }
                    else if (consumeResult.Disposition == CMqConsumptionDisposition.DeadLetter)
                    {
                        Statistics.ExplicitDeadLetterCount++;
                        message.State = CMqMessageState.DeadLetter;
                        break;
                    }
                    else if (consumeResult.Disposition == CMqConsumptionDisposition.Drop)
                    {
                        Statistics.ExplicitDropCount++;
                        message.State = CMqMessageState.Drop;
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
                    message.FailedSubscriberIDs.Add(subscriber.SubscriberId);
                    message.SatisfiedSubscriberIDs.Add(subscriber.SubscriberId);
                }

                if (allSubscriberIDs.Except(message.SatisfiedSubscriberIDs).Any() == false)
                {
                    //All subscribers have received a copy of the message or have received their maximum number
                    //  of retries, so we can now remove the message from the queue.

                    //If there were any failed deliveries, then we want to retain a copy of the message in the DLQ.
                    if (message.FailedSubscriberIDs.Count != 0)
                    {
                        if (Configuration.DeadLetterConfiguration != null)
                        {
                            if (Configuration.DeadLetterConfiguration.MaxMessageAge != null
                                && (DateTime.UtcNow - message.Timestamp) > Configuration.DeadLetterConfiguration.MaxMessageAge)
                            {
                                message.State = CMqMessageState.Drop;
                                break;
                            }
                            else
                            {
                                message.State = CMqMessageState.DeadLetter;
                                break;
                            }
                        }
                    }

                    //Message was successfully delivered to all subscribers.
                    message.State = CMqMessageState.Drop;
                    break;
                }
            }
        }

        /// <summary>
        /// Load additional messages into the message buffer from the database.
        /// </summary>
        private bool HydrateMessageBuffer(int countToGet)
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
                    throw new Exception($"Persistence database has not been initialized for [{Configuration.QueueName}].");
                }

                ulong? maxBufferedSerialNumber = m.MessageBuffer.Count > 0 ? m.MessageBuffer.Max(o => o.SerialNumber) : null;
                int messagesLoaded = 0;

                using var iterator = m.Database.NewIterator();
                for (iterator.SeekToFirst(); iterator.Valid() && messagesLoaded < countToGet; iterator.Next())
                {
                    var persistedSerialNumberBytes = iterator.Key();
                    if (BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(persistedSerialNumberBytes); // Convert key back to little-endian.
                    }
                    var persistedSerialNumber = BitConverter.ToUInt64(persistedSerialNumberBytes);

                    if (maxBufferedSerialNumber == null || persistedSerialNumber > maxBufferedSerialNumber)
                    {
                        var json = Encoding.UTF8.GetString(iterator.Value());
                        var persistedMessage = JsonSerializer.Deserialize<EnqueuedMessage>(json);
                        if (persistedMessage != null)
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

            _queueServer.InvokeOnLog(CMqErrorLevel.Information, $"Creating persistent path for [{Configuration.QueueName}].");

            var databasePath = Path.Join(_queueServer.Configuration.PersistencePath, "messages", Configuration.QueueName);
            Directory.CreateDirectory(databasePath);

            _queueServer.InvokeOnLog(CMqErrorLevel.Information, $"Instantiating persistent database for [{Configuration.QueueName}].");
            var options = new DbOptions().SetCreateIfMissing(true);
            var persistenceDatabase = RocksDb.Open(options, databasePath);

            _queueServer.InvokeOnLog(CMqErrorLevel.Information, $"Loading persistent messages for [{Configuration.QueueName}].");

            EnqueuedMessages.Write(m =>
            {
                Statistics.SetQueueDepth(0);

                int messagesLoaded = 0;
                var lastSerialNumberBytes = BitConverter.GetBytes((ulong)0);

                using var iterator = persistenceDatabase.NewIterator();
                for (iterator.SeekToFirst(); iterator.Valid(); iterator.Next())
                {
                    lastSerialNumberBytes = iterator.Key();
                    messagesLoaded++;
                }

                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(lastSerialNumberBytes); // Convert back to little-endian.
                }
                var lastSerialNumber = BitConverter.ToUInt64(lastSerialNumberBytes);

                _queueServer.InvokeOnLog(CMqErrorLevel.Information, $"Loaded {messagesLoaded:n0} messages for [{Configuration.QueueName}] with serial number 0x{lastSerialNumber:x}.");

                Statistics.SetQueueDepth(messagesLoaded);
                Statistics.SetLastSerialNumber(lastSerialNumber);

                m.Database = persistenceDatabase;
            });
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
