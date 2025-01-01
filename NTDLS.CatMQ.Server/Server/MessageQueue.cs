using NTDLS.CatMQ.Server.Management;
using NTDLS.CatMQ.Shared;
using NTDLS.Helpers;
using NTDLS.Semaphore;

namespace NTDLS.CatMQ.Server.Server
{
    /// <summary>
    /// A named message queue and its delivery thread.
    /// </summary>
    internal class MessageQueue
    {
        private readonly Thread _deliveryThread = new(DeliveryThreadProc);
        private CMqServer? QueueServer { get; set; }

        internal AutoResetEvent DeliveryThreadWaitEvent = new(false);
        internal bool KeepRunning { get; set; } = false;

        /// <summary>
        /// List of subscriber connection IDs.
        /// </summary>
        internal PessimisticCriticalResource<Dictionary<Guid, CMqSubscriberInformation>> Subscribers { get; set; } = new();

        /// <summary>
        /// Messages that are enqueued in this list.
        /// </summary>
        internal PessimisticCriticalResource<List<EnqueuedMessage>> EnqueuedMessages { get; set; } = new();

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
        }

        public MessageQueue(CMqServer mqServer, CMqQueueConfiguration queueConfiguration)
        {
            QueueServer = mqServer;
            QueueConfiguration = queueConfiguration;

        }

        internal void SetServer(CMqServer mqServer)
        {
            QueueServer = mqServer;
        }

        /// <summary>
        /// Sorts the messages in the queue by their timestamps.
        /// This is only necessary when loading massages from the database at startup.
        /// </summary>
        internal void SortMessages()
        {
            EnqueuedMessages.Use(m =>
            {
                m.Sort((m1, m2) => m1.Timestamp.CompareTo(m2.Timestamp));
            });
        }

        private static void DeliveryThreadProc(object? pMessageQueue)
        {
            var messageQueue = pMessageQueue.EnsureNotNull<MessageQueue>();

            var lastStaleMessageScan = DateTime.UtcNow;

            messageQueue.QueueServer.EnsureNotNull();

            while (messageQueue.KeepRunning)
            {
                int successfulDeliveries = 0; //Just used to omit waiting. We want to spin fast when we are delivering messages.

                try
                {
                    EnqueuedMessage? topMessage = null;
                    List<CMqSubscriberInformation>? yetToBeDeliveredSubscribers = null;

                    #region Get top message and its subscribers.

                    messageQueue.EnqueuedMessages.TryUseAll([messageQueue.Subscribers], m =>
                    {
                        messageQueue.Subscribers.Use(s => //This lock is already held.
                        {
                            if (messageQueue.QueueConfiguration.MaxMessageAge > TimeSpan.Zero
                            && (DateTime.UtcNow - lastStaleMessageScan).TotalSeconds >= 10)
                            {
                                //If MaxMessageAge is defined, then remove stale messages.

                                var expiredMessageIDs = m.Where(o => (DateTime.UtcNow - o.Timestamp) > messageQueue.QueueConfiguration.MaxMessageAge)
                                    .Select(o => o.MessageId).ToHashSet();

                                foreach (var messageId in expiredMessageIDs)
                                {
                                    messageQueue.QueueServer.RemovePersistenceMessage(messageQueue.QueueConfiguration.QueueName, messageId);
                                }

                                messageQueue.ExpiredMessageCount += (ulong)m.RemoveAll(o => expiredMessageIDs.Contains(o.MessageId));

                                //There could be a lot of messages in the queue, so lets use lastStaleMessageScan
                                //  to not needlessly compare the timestamps each-and-every loop.
                                lastStaleMessageScan = DateTime.UtcNow;
                            }

                            //We only process a queue if it has subscribers.
                            //This is so we do not discard messages as delivered for queues with no subscribers.
                            if (s.Count > 0)
                            {
                                topMessage = m.FirstOrDefault(); //Get the first message in the list, if any.

                                if (topMessage != null)
                                {
                                    //Get list of subscribers that have yet to get a copy of the message.

                                    var yetToBeDeliveredSubscriberIds = s.Keys.Except(topMessage.SatisfiedSubscriberssubscriberIDs).ToList();

                                    yetToBeDeliveredSubscribers = s.Where(o => yetToBeDeliveredSubscriberIds.Contains(o.Key)).Select(o => o.Value).ToList();

                                    if (messageQueue.QueueConfiguration.DeliveryScheme == CMqDeliveryScheme.Balanced)
                                    {
                                        yetToBeDeliveredSubscribers = yetToBeDeliveredSubscribers.OrderBy(_ => Guid.NewGuid()).ToList();
                                    }
                                }
                            }
                        });
                    });

                    #endregion

                    //We we have a message, deliver it to the queue subscribers.
                    if (topMessage != null && yetToBeDeliveredSubscribers != null)
                    {
                        bool successfulDeliveryAndConsume = false;

                        #region Deliver message to subscibers.

                        foreach (var subscriber in yetToBeDeliveredSubscribers)
                        {
                            if (messageQueue.KeepRunning == false)
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

                                if (messageQueue.QueueServer.DeliverMessage(subscriber.SubscriberId, messageQueue.QueueConfiguration.QueueName, topMessage))
                                {
                                    subscriber.ConsumedMessages++;
                                    successfulDeliveryAndConsume = true;
                                }

                                messageQueue.DeliveredMessageCount++;
                                subscriber.SuccessfulMessagesDeliveries++;

                                if (messageQueue.KeepRunning == false)
                                {
                                    break;
                                }

                                //This thread is the only place we manage [SatisfiedSubscribersConnectionIDs], so we can use it without additional locking.
                                topMessage.SatisfiedSubscriberssubscriberIDs.Add(subscriber.SubscriberId);
                                successfulDeliveries++;

                                if (successfulDeliveryAndConsume
                                    && messageQueue.QueueConfiguration.ConsumptionScheme == CMqConsumptionScheme.FirstConsumedSubscriber)
                                {
                                    //Message was delivered and consumed, break the delivery loop so the message can be removed from the queue.
                                    break;
                                }
                            }
                            catch (Exception ex) //Delivery failure.
                            {
                                messageQueue.DeliveryFailureCount++;
                                subscriber.FailedMessagesDeliveries++;
                                messageQueue.QueueServer.InvokeOnLog(messageQueue.QueueServer, ex.GetBaseException());
                            }

                            //If we have tried to deliver this message to this subscriber too many times, then mark this subscriber-message as satisfied.
                            if (messageQueue.QueueConfiguration.MaxDeliveryAttempts > 0
                                && subscriberMessageDelivery.DeliveryAttempts >= messageQueue.QueueConfiguration.MaxDeliveryAttempts)
                            {
                                topMessage.SatisfiedSubscriberssubscriberIDs.Add(subscriber.SubscriberId);
                            }
                        }

                        #endregion

                        #region Delivery Throttle.

                        if (messageQueue.QueueConfiguration.DeliveryThrottle > TimeSpan.Zero)
                        {
                            if (messageQueue.QueueConfiguration.DeliveryThrottle.TotalSeconds >= 1)
                            {
                                int sleepSeconds = (int)messageQueue.QueueConfiguration.DeliveryThrottle.TotalSeconds;
                                for (int sleep = 0; sleep < sleepSeconds && messageQueue.KeepRunning; sleep++)
                                {
                                    Thread.Sleep(1000);
                                }
                            }
                            else
                            {
                                Thread.Sleep((int)messageQueue.QueueConfiguration.DeliveryThrottle.TotalMilliseconds);
                            }
                        }

                        #endregion

                        #region Remove message from queue.

                        messageQueue.EnqueuedMessages.UseAll([messageQueue.Subscribers], m =>
                        {
                            messageQueue.Subscribers.Use(s => //This lock is already held.
                            {
                                if (successfulDeliveryAndConsume && messageQueue.QueueConfiguration.ConsumptionScheme == CMqConsumptionScheme.FirstConsumedSubscriber)
                                {
                                    //The message was consumed by a subscriber, remove it from the message list.
                                    messageQueue.QueueServer.RemovePersistenceMessage(messageQueue.QueueConfiguration.QueueName, topMessage.MessageId);
                                    m.Remove(topMessage);
                                }
                                else if (s.Keys.Except(topMessage.SatisfiedSubscriberssubscriberIDs).Any() == false)
                                {
                                    //If all subscribers are satisfied (delivered or max attempts reached), then remove the message.
                                    messageQueue.QueueServer.RemovePersistenceMessage(messageQueue.QueueConfiguration.QueueName, topMessage.MessageId);
                                    m.Remove(topMessage);
                                }
                            });
                        });

                        #endregion

                        if (messageQueue.KeepRunning == false)
                        {
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    messageQueue.QueueServer.InvokeOnLog(messageQueue.QueueServer, ex.GetBaseException());
                }

                if (successfulDeliveries == 0)
                {
                    //If nothing was successfully delivered, then delay for a period.
                    messageQueue.DeliveryThreadWaitEvent.WaitOne(10);
                }
            }
        }

        public void Start()
        {
            KeepRunning = true;
            _deliveryThread.Start(this);
        }

        public void Stop()
        {
            KeepRunning = false;
            _deliveryThread.Join();
        }
    }
}
