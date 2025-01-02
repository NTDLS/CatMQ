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
        private CMqServer? _queueServer;

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

        public CMqQueueConfiguration Configuration { get; set; }

        public MessageQueueStatistics Statistics { get; set; } = new();

        public MessageQueue()
        {
            Configuration = new();
            _deliveryThread = new(DeliveryThreadProc);
        }

        public MessageQueue(CMqServer mqServer, CMqQueueConfiguration queueConfiguration)
        {
            _queueServer = mqServer;
            Configuration = queueConfiguration;
            _deliveryThread = new(DeliveryThreadProc);
        }

        internal void SetServer(CMqServer mqServer)
        {
            _queueServer = mqServer;
        }

        private void DeliveryThreadProc(object? p)
        {
            _queueServer.EnsureNotNull();

            LoadPersistentMessages();

            var lastCheckpoint = DateTime.UtcNow;

            while (KeepRunning)
            {
                if (DateTime.UtcNow - lastCheckpoint > TimeSpan.FromSeconds(30))
                {
                    if (Configuration.PersistenceScheme == CMqPersistenceScheme.Persistent)
                    {
                        EnqueuedMessages.TryWrite(o =>
                        {
                            if (o.Database != null)
                            {
                                //While the RockDB WAL logs data, it’s a good idea to flush the MemTable to disk periodically for additional safety.
                                _queueServer.InvokeOnLog(CMqErrorLevel.Verbose, $"Checkpoint persistent database for [{Configuration.QueueName}].");
                                o.Database.Flush(new FlushOptions());
                            }
                        });
                    }

                    lastCheckpoint = DateTime.UtcNow;
                }

                bool shouldSleep = true; //Just used to omit waiting. We want to spin fast when we are delivering messages.

                try
                {
                    EnqueuedMessage? topMessage = null;
                    List<CMqSubscriberDescriptor>? yetToBeDeliveredSubscribers = null;

                    #region Get top message and its subscribers.

                    EnqueuedMessages.TryReadAll([Subscribers], m =>
                    {
                        Subscribers.Read(s =>
                        {
                            //We only process a queue if it has subscribers.
                            //This is so we do not discard messages as delivered for queues with no subscribers.
                            if (s.Count > 0)
                            {
                                topMessage = m.Messages.FirstOrDefault(); //Get the first message in the list, if any.

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

                                    m.Database?.Remove(testExpired.MessageId.ToString());
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
                        bool successfulDeliveryAndConsume = false;

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
                                subscriber.DeliveryAttempts++;

                                if (_queueServer.DeliverMessage(subscriber.SubscriberId, Configuration.QueueName, topMessage))
                                {
                                    subscriber.ConsumedMessages++;
                                    successfulDeliveryAndConsume = true;
                                }

                                Statistics.DeliveredMessageCount++;
                                subscriber.SuccessfulMessagesDeliveries++;

                                if (KeepRunning == false)
                                {
                                    break;
                                }

                                //This thread is the only place we manage [SatisfiedSubscribersConnectionIDs], so we can use it without additional locking.
                                topMessage.SatisfiedSubscribersSubscriberIDs.Add(subscriber.SubscriberId);
                                shouldSleep = false;

                                if (successfulDeliveryAndConsume
                                    && Configuration.ConsumptionScheme == CMqConsumptionScheme.FirstConsumedSubscriber)
                                {
                                    //Message was delivered and consumed, break the delivery loop so the message can be removed from the queue.
                                    break;
                                }
                            }
                            catch (Exception ex) //Delivery failure.
                            {
                                Statistics.DeliveryFailureCount++;
                                subscriber.FailedMessagesDeliveries++;
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
                                removeSuccess = Subscribers.TryRead(s =>
                                {
                                    if (successfulDeliveryAndConsume && Configuration.ConsumptionScheme == CMqConsumptionScheme.FirstConsumedSubscriber)
                                    {
                                        //The message was consumed by a subscriber, remove it from the message list.
                                        m.Database?.Remove(topMessage.MessageId.ToString());
                                        m.Messages.Remove(topMessage);
                                    }
                                    else if (s.Keys.Except(topMessage.SatisfiedSubscribersSubscriberIDs).Any() == false)
                                    {
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

                                        //If all subscribers are satisfied (delivered or max attempts reached), then remove the message.
                                        m.Database?.Remove(topMessage.MessageId.ToString());
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

        private void LoadPersistentMessages()
        {
            _queueServer.EnsureNotNull();

            var deadLetterQueueMessages = new Dictionary<string, List<EnqueuedMessage>>(StringComparer.OrdinalIgnoreCase);

            #region Load persisted messages.

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
                        m.Database?.Remove(deadLetterMessage.MessageId.ToString());
                    }
                }

                m.Database = persistenceDatabase;
            });

            #endregion
        }

        public void StartAsync()
        {
            _queueServer?.InvokeOnLog(CMqErrorLevel.Information, $"Starting delivery thread for [{Configuration.QueueName}].");
            KeepRunning = true;
            _deliveryThread.Start();
        }

        public void StopAsync()
        {
            _queueServer?.InvokeOnLog(CMqErrorLevel.Information, $"Signaling shutdown for [{Configuration.QueueName}].");
            KeepRunning = false;
        }

        public void WaitOnStop()
        {
            _queueServer?.InvokeOnLog(CMqErrorLevel.Information, $"Waiting on delivery thread to quit for [{Configuration.QueueName}].");
            _deliveryThread.Join();

            _queueServer?.InvokeOnLog(CMqErrorLevel.Information, $"Shutting down database connection for [{Configuration.QueueName}].");
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
