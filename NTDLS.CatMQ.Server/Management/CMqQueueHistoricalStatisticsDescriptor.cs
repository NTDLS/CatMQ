namespace NTDLS.CatMQ.Server.Management
{
    /// <summary>
    /// Contains information about the performance of a queue.
    /// </summary>
    public class CMqQueueHistoricalStatisticsDescriptor: ICloneable
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
        /// Returns a copy of the CMqQueueHistoricalStatisticsDescriptor.
        /// </summary>
        /// <returns></returns>
        public object Clone()
        {
            return new CMqQueueHistoricalStatisticsDescriptor
            {
                EnqueuedCount = EnqueuedCount,
                DeliveryCount = DeliveryCount,
                DequeuedCount = DequeuedCount,
            };
        }
    }
}
