using NTDLS.CatMQ.Server.Management;
using NTDLS.CatMQ.Shared;
using NTDLS.Helpers;
using NTDLS.Semaphore;
using System.Reflection.Metadata.Ecma335;

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
        internal OptimisticCriticalResource<Dictionary<Guid, CMqSubscriberInformation>> Subscribers { get; set; } = new();

        /// <summary>
        /// Messages that are enqueued in this list.
        /// </summary>
        internal OptimisticCriticalResource<List<EnqueuedMessage>> EnqueuedMessages { get; set; } = new();

        public CMqQueueConfiguration QueueConfiguration { get; set; }

        /// <summary>
        /// The total number of messages that have been enqueued into this queue.
        /// </summary>
        public ulong ReceivedMessageCount { get; set; }

        /// <summary>
        /// The total number of messages that have been removed from this queue due to age expiration.
        /// </summary>
        public ulong ExpiredMessageCount { get; set; }

        /// <summary>
        /// The total number of messages that have been delivered from this queue to subscribers.
        /// </summary>
        public ulong DeliveredMessageCount { get; set; }

        /// <summary>
        /// The total number of messages that have failed to deliver from this queue to subscribers.
        /// </summary>
        public ulong DeliveryFailureCount { get; set; }

        public MessageQueue()
        {
            QueueConfiguration = new();
            _deliveryThread = new(DeliveryThreadProc);
        }

        public MessageQueue(CMqServer mqServer, CMqQueueConfiguration queueConfiguration)
        {
            _queueServer = mqServer;
            QueueConfiguration = queueConfiguration;
            _deliveryThread = new(DeliveryThreadProc);
        }

        internal void SetServer(CMqServer mqServer)
        {
            _queueServer = mqServer;
        }

        /// <summary>
        /// Sorts the messages in the queue by their timestamps.
        /// This is only necessary when loading massages from the database at startup.
        /// </summary>
        internal void SortMessages()
        {
            EnqueuedMessages.Write(m =>
            {
                m.Sort((m1, m2) => m1.Timestamp.CompareTo(m2.Timestamp));
            });
        }

        private void DeliveryThreadProc(object? p)
        {
            _queueServer.EnsureNotNull();

            while (KeepRunning)
            {
                bool shouldSleep = true; //Just used to omit waiting. We want to spin fast when we are delivering messages.

                try
                {
                    EnqueuedMessage? topMessage = null;
                    List<CMqSubscriberInformation>? yetToBeDeliveredSubscribers = null;

                    #region Get top message and its subscribers.

                    EnqueuedMessages.TryReadAll([Subscribers], m =>
                    {
                        Subscribers.Read(s => //This lock is already held.
                        {
                            //We only process a queue if it has subscribers.
                            //This is so we do not discard messages as delivered for queues with no subscribers.
                            if (s.Count > 0)
                            {
                                topMessage = m.FirstOrDefault(); //Get the first message in the list, if any.

                                if (topMessage != null)
                                {
                                    //Get list of subscribers that have yet to get a copy of the message.

                                    var yetToBeDeliveredSubscriberIds = s.Keys.Except(topMessage.SatisfiedSubscribersSubscriberIDs).ToList();

                                    yetToBeDeliveredSubscribers = s.Where(o => yetToBeDeliveredSubscriberIds.Contains(o.Key)).Select(o => o.Value).ToList();

                                    if (QueueConfiguration.DeliveryScheme == CMqDeliveryScheme.Balanced)
                                    {
                                        yetToBeDeliveredSubscribers = yetToBeDeliveredSubscribers.OrderBy(_ => Guid.NewGuid()).ToList();
                                    }
                                }
                            }
                        });

                        if (QueueConfiguration.MaxMessageAge > TimeSpan.Zero)
                        {
                            //Get the first message in the list, if any.
                            //We do this so that we can test for expired messages even when no subscribers are present.
                            var testExpired = topMessage ?? m.FirstOrDefault();

                            if (testExpired != null)
                            {
                                if ((DateTime.UtcNow - testExpired.Timestamp) > QueueConfiguration.MaxMessageAge)
                                {
                                    //If MaxMessageAge is defined, then remove the stale messages.

                                    _queueServer.RemovePersistenceMessage(QueueConfiguration.QueueName, testExpired.MessageId);
                                    _queueServer.ShovelToDLQ(QueueConfiguration.QueueName,
                                        $"{QueueConfiguration.QueueName.ToLowerInvariant()}.dlq", testExpired);

                                    m.Remove(testExpired);
                                    ExpiredMessageCount++;

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

                                if (_queueServer.DeliverMessage(subscriber.SubscriberId, QueueConfiguration.QueueName, topMessage))
                                {
                                    subscriber.ConsumedMessages++;
                                    successfulDeliveryAndConsume = true;
                                }

                                DeliveredMessageCount++;
                                subscriber.SuccessfulMessagesDeliveries++;

                                if (KeepRunning == false)
                                {
                                    break;
                                }

                                //This thread is the only place we manage [SatisfiedSubscribersConnectionIDs], so we can use it without additional locking.
                                topMessage.SatisfiedSubscribersSubscriberIDs.Add(subscriber.SubscriberId);
                                shouldSleep = false;

                                if (successfulDeliveryAndConsume
                                    && QueueConfiguration.ConsumptionScheme == CMqConsumptionScheme.FirstConsumedSubscriber)
                                {
                                    //Message was delivered and consumed, break the delivery loop so the message can be removed from the queue.
                                    break;
                                }
                            }
                            catch (Exception ex) //Delivery failure.
                            {
                                DeliveryFailureCount++;
                                subscriber.FailedMessagesDeliveries++;
                                _queueServer.InvokeOnLog(_queueServer, ex.GetBaseException());
                            }

                            //If we have tried to deliver this message to this subscriber too many times, then mark this subscriber-message as satisfied.
                            if (QueueConfiguration.MaxDeliveryAttempts > 0
                                && subscriberMessageDelivery.DeliveryAttempts >= QueueConfiguration.MaxDeliveryAttempts)
                            {
                                topMessage.FailedSubscribersSubscriberIDs.Add(subscriber.SubscriberId);
                                topMessage.SatisfiedSubscribersSubscriberIDs.Add(subscriber.SubscriberId);
                            }
                        }

                        #endregion

                        #region Delivery Throttle.

                        if (QueueConfiguration.DeliveryThrottle > TimeSpan.Zero)
                        {
                            if (QueueConfiguration.DeliveryThrottle.TotalSeconds >= 1)
                            {
                                int sleepSeconds = (int)QueueConfiguration.DeliveryThrottle.TotalSeconds;
                                for (int sleep = 0; sleep < sleepSeconds && KeepRunning; sleep++)
                                {
                                    Thread.Sleep(1000);
                                }
                            }
                            else
                            {
                                Thread.Sleep((int)QueueConfiguration.DeliveryThrottle.TotalMilliseconds);
                            }
                        }

                        #endregion

                        #region Remove message from queue.

                        bool removeSuccess = true;

                        do
                        {
                            removeSuccess = EnqueuedMessages.TryWrite(m =>
                            {
                                removeSuccess = Subscribers.TryRead(s => //This lock is already held.
                                {
                                    if (successfulDeliveryAndConsume && QueueConfiguration.ConsumptionScheme == CMqConsumptionScheme.FirstConsumedSubscriber)
                                    {
                                        //The message was consumed by a subscriber, remove it from the message list.
                                        _queueServer.RemovePersistenceMessage(QueueConfiguration.QueueName, topMessage.MessageId);
                                        m.Remove(topMessage);
                                    }
                                    else if (s.Keys.Except(topMessage.SatisfiedSubscribersSubscriberIDs).Any() == false)
                                    {
                                        if (topMessage.FailedSubscribersSubscriberIDs.Count != 0)
                                        {
                                            _queueServer.ShovelToDLQ(QueueConfiguration.QueueName,
                                                $"{QueueConfiguration.QueueName.ToLowerInvariant()}.dlq", topMessage);
                                        }

                                        //If all subscribers are satisfied (delivered or max attempts reached), then remove the message.
                                        _queueServer.RemovePersistenceMessage(QueueConfiguration.QueueName, topMessage.MessageId);
                                        m.Remove(topMessage);
                                    }
                                }) && removeSuccess;
                            }) && removeSuccess;

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
                    _queueServer.InvokeOnLog(_queueServer, ex.GetBaseException());
                }

                if (shouldSleep)
                {
                    //If nothing was successfully delivered, then delay for a period.
                    DeliveryThreadWaitEvent.WaitOne(10);
                }
            }
        }

        public void StartAsync()
        {
            KeepRunning = true;
            _deliveryThread.Start();
        }

        public void StopAsync()
        {
            KeepRunning = false;
            _deliveryThread.Join();
        }

        public void WaitOnStop()
        {
            _deliveryThread.Join();
        }
    }
}
