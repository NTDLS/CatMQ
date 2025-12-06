namespace NTDLS.CatMQ.Server.Management
{
    /// <summary>
    /// Contains information about the performance of a queue.
    /// </summary>
    public class CMqPerQueueHistoricalStatisticsDescriptor : ICloneable
    {
        /// <summary>
        /// The number of messages received by a client.
        /// </summary>
        public long EnqueuedCount;
        internal void IncrementEnqueuedCount() => Interlocked.Increment(ref EnqueuedCount);

        /// <summary>
        /// The number of messages delivered to a subscriber.
        /// </summary>
        public long DeliveryCount;
        internal void IncrementDeliveryCount() => Interlocked.Increment(ref DeliveryCount);

        /// <summary>
        /// The number of messages dequeued due to successfully deleivery to all requied subscribers, deadletter or expiration.
        /// </summary>
        public long DequeuedCount;
        internal void IncrementDequeuedCount() => Interlocked.Increment(ref DequeuedCount);

        /// <summary>
        /// The current depth of the queue.
        /// </summary>
        public long QueueDepth;
        internal void SetQueueDepth(long value) => Interlocked.Exchange(ref QueueDepth, value);

        /// <summary>
        /// The number of subscriber for this queue.
        /// </summary>
        public long SubscriberCount;
        internal void SetSubscriberCount(long value) => Interlocked.Exchange(ref SubscriberCount, value);

        /// <summary>
        /// The number of asynchronous deliveries that are currently outstanding.
        /// </summary>
        public long OutstandingDeliveries;
        internal void SetOutstandingDeliveries(long value) => Interlocked.Exchange(ref OutstandingDeliveries, value);

        /// <summary>
        /// The total number of times a subscriber has requested that an attempted delivery be deferred to a later time.
        /// </summary>
        public long DeferredDeliveries;
        internal void IncrementDeferredDeliveries() => Interlocked.Increment(ref DeferredDeliveries);

        internal void SetDescreteValues(CMqQueueDescriptor queueDescriptor)
        {
            SetQueueDepth(queueDescriptor.QueueDepth);
            SetSubscriberCount(queueDescriptor.CurrentSubscriberCount);
            SetOutstandingDeliveries(queueDescriptor.CurrentOutstandingDeliveries);
        }

        /// <summary>
        /// Returns a copy of the CMqQueueHistoricalStatisticsDescriptor.
        /// </summary>
        /// <returns></returns>
        public object Clone()
        {
            return new CMqPerQueueHistoricalStatisticsDescriptor
            {
                EnqueuedCount = EnqueuedCount,
                DeliveryCount = DeliveryCount,
                DequeuedCount = DequeuedCount,
                QueueDepth = QueueDepth,
                SubscriberCount = SubscriberCount,
                OutstandingDeliveries = OutstandingDeliveries,
                DeferredDeliveries = DeferredDeliveries
            };
        }
    }
}
