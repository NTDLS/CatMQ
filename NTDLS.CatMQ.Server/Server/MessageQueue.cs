using NTDLS.CatMQ.Server.Management;
using NTDLS.CatMQ.Shared;
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

                    EnqueuedMessages.TryReadAll([Subscribers], CMqDefaults.DEFAULT_TRY_WAIT_MS, m =>
                    {
                        Subscribers.Read(s =>
                        {
                            //We only process a queue if it has subscribers.
                            //This is so we do not discard messages as delivered for queues with no subscribers.
                            if (s.Count > 0)
                            {
                                //Get the first message in the list, if any.
                                topMessage = m.Messages.FirstOrDefault(o => o.DeferredUntil == null || now >= o.DeferredUntil);

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
                            var testExpired = topMessage ?? m.Messages.FirstOrDefault();

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
                                    m.Messages.Remove(testExpired);
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
                                subscriberMessageDelivery.DeliveryAttempts++;
                            }
                            else
                            {
                                subscriberMessageDelivery = new SubscriberMessageDelivery() { DeliveryAttempts = 1 };
                                topMessage.SubscriberMessageDeliveries.Add(subscriber.SubscriberId, subscriberMessageDelivery);
                            }

                            try
                            {
                                subscriber.AttemptedDeliveryCount++;

                                var consumeResult = _queueServer.DeliverMessage(subscriber.SubscriberId, Configuration.QueueName, topMessage);

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

                                Statistics.DeliveredMessageCount++;
                                subscriber.SuccessfulDeliveryCount++;

                                if (KeepRunning == false)
                                {
                                    break;
                                }
                            }
                            catch (Exception ex) //Delivery failure.
                            {
                                Statistics.FailedDeliveryCount++;
                                subscriber.FailedDeliveryCount++;
                                _queueServer.InvokeOnLog(ex.GetBaseException());
                            }

                            //If we have tried to deliver this message to this subscriber too many times, then mark this subscriber-message as satisfied.
                            if (Configuration.MaxDeliveryAttempts > 0
                                && subscriberMessageDelivery.DeliveryAttempts >= Configuration.MaxDeliveryAttempts)
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
                                        m.Messages.Remove(topMessage);
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
                                        m.Messages.Remove(topMessage);
                                    }
                                    else if (Configuration.ConsumptionScheme == CMqConsumptionScheme.FirstConsumedSubscriber)
                                    {
                                        //If the queue uses the FirstConsumedSubscriber schema and the message was successfully consumed
                                        //  by a subscriber, then remove the message from the queue.
                                        if (deliveredAndConsumed)
                                        {
                                            //The message was consumed by a subscriber, remove it from the queue and cache.
                                            m.Database?.Remove(topMessage.SerialNumber.ToString());
                                            m.Messages.Remove(topMessage);
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
                                        m.Messages.Remove(topMessage);
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

        public void InitializePersistentDatabase()
        {
            if (Configuration.PersistenceScheme != CMqPersistenceScheme.Persistent)
            {
                return;
            }

            var deadLetterQueueMessages = new Dictionary<string, List<EnqueuedMessage>>(StringComparer.OrdinalIgnoreCase);

            #region Load persisted messages.

            _queueServer.InvokeOnLog(CMqErrorLevel.Verbose, $"Creating persistent path for [{Configuration.QueueName}].");

            var databasePath = Path.Join(_queueServer.Configuration.PersistencePath, "messages", Configuration.QueueName);
            Directory.CreateDirectory(databasePath);

            _queueServer.InvokeOnLog(CMqErrorLevel.Information, $"Instantiating persistent database for [{Configuration.QueueName}].");
            var options = new DbOptions().SetCreateIfMissing(true);
            var persistenceDatabase = RocksDb.Open(options, databasePath);

            _queueServer.InvokeOnLog(CMqErrorLevel.Information, $"Loading persistent messages for [{Configuration.QueueName}].");
            using var iterator = persistenceDatabase.NewIterator();

            EnqueuedMessages.Write(m =>
            {
                //The keys in RocksDB are not stored in the order they were added, so we
                // need to load all messages into the messages queues then sort them in place.
                for (iterator.SeekToFirst(); iterator.Valid(); iterator.Next())
                {
                    //Deserialize using System.Text.Json as opposed to Newtonsoft for efficiency.
                    var persistedMessage = JsonSerializer.Deserialize<EnqueuedMessage>(iterator.StringValue());
                    if (persistedMessage != null)
                    {
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
                                    else if (deadLetterQueueMessages.TryGetValue(persistedMessage.QueueName, out var deadLetterMessages))
                                    {
                                        //Add to dead-letter queue.
                                        deadLetterMessages.Add(persistedMessage);
                                    }
                                    else
                                    {
                                        //Add to dead-letter queue.
                                        deadLetterQueueMessages.Add(persistedMessage.QueueName, new List<EnqueuedMessage> { persistedMessage });
                                    }
                                }
                                else
                                {
                                    //No dead-letter queue, discard expired message.
                                }
                            }
                            else
                            {
                                //Add the message back to its original queue.
                                m.Messages.Add(persistedMessage);
                            }
                        }
                        else
                        {
                            //Add the message back to its original queue.
                            m.Messages.Add(persistedMessage);
                        }
                    }
                }

                //Sort the message in the queues by their timestamps.
                _queueServer.InvokeOnLog(CMqErrorLevel.Information, $"Sorting {m.Messages.Count:n0} messages for [{Configuration.QueueName}].");
                m.Messages.Sort((m1, m2) => m1.Timestamp.CompareTo(m2.Timestamp));

                if (deadLetterQueueMessages.TryGetValue(Configuration.QueueName, out var expiredMessages))
                {
                    _queueServer.InvokeOnLog(CMqErrorLevel.Information, $"Dead-lettering {expiredMessages.Count:n0} expired messages in [{Configuration.QueueName}].");

                    foreach (var deadLetterMessage in expiredMessages)
                    {
                        Statistics.ExpiredMessageCount++;
                        _queueServer.ShovelToDeadLetter(Configuration.QueueName, deadLetterMessage);
                        m.Database?.Remove(deadLetterMessage.SerialNumber.ToString());
                    }
                }

                m.Database = persistenceDatabase;
            });

            #endregion
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
