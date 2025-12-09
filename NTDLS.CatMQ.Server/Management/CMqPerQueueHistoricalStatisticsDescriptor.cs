namespace NTDLS.CatMQ.Server.Management
{
    /// <summary>
    /// Contains information about the performance of a queue.
    /// </summary>
    public class CMqPerQueueHistoricalStatisticsDescriptor
    {
        /// <summary>
        /// The number of samples that were used to generate the statistics.
        /// </summary>
        public int Samples { get; set; }

        /// <summary>
        /// The rate for which messages are received by the queue (messages per second).
        /// </summary>
        public double ReceiveRate { get; set; }
        /// <summary>
        /// The rate for which messages are delivered to the subscribers (messages per second).
        /// </summary>
        public double DeliveryRate { get; set; }
        /// <summary>
        /// The rate at which the messages are expiring from the queue (messages per second).
        /// </summary>
        public double ExpiracyRate { get; set; }
        /// <summary>
        /// The rate at which message deliveries are failing (messages per second).
        /// </summary>
        public double FailedDeliveryRate { get; set; }
        /// <summary>
        /// The rate at which message deliveries are being deferred (messages per second).
        /// </summary>
        public double DeferredDeliveryRate { get; set; }
        /// <summary>
        /// The rate at which messages are being explicitedly dead-lettered by the subscriber (messages per second).
        /// </summary>
        public double ExplicitDeadLetterRate { get; set; }
        /// <summary>
        /// The rate at which messages are being explicitedly dropped (messages per second).
        /// </summary>
        public double ExplicitDropRate { get; set; }
        /// <summary>
        /// The current depth of the queue.
        /// </summary>
        public long QueueDepth { get; set; }
        /// <summary>
        /// The number of asynchronous deliveries that are currently outstanding.
        /// </summary>
        public long OutstandingDeliveries { get; set; }

        internal void Merge(CMqPerQueueHistoricalStatisticsDescriptor other)
        {
            ReceiveRate += other.ReceiveRate;
            DeliveryRate += other.DeliveryRate;
            ExpiracyRate += other.ExpiracyRate;
            FailedDeliveryRate += other.FailedDeliveryRate;
            DeferredDeliveryRate += other.DeferredDeliveryRate;
            ExplicitDeadLetterRate += other.ExplicitDeadLetterRate;
            ExplicitDropRate += other.ExplicitDropRate;
            QueueDepth += other.QueueDepth;
            OutstandingDeliveries += other.OutstandingDeliveries;
        }

        internal CMqPerQueueHistoricalStatisticsDescriptor Clone()
        {
            return new CMqPerQueueHistoricalStatisticsDescriptor()
            {
                ReceiveRate = this.ReceiveRate,
                DeliveryRate = this.DeliveryRate,
                ExpiracyRate = this.ExpiracyRate,
                FailedDeliveryRate = this.FailedDeliveryRate,
                DeferredDeliveryRate = this.DeferredDeliveryRate,
                ExplicitDeadLetterRate = this.ExplicitDeadLetterRate,
                ExplicitDropRate = this.ExplicitDropRate,
                QueueDepth = this.QueueDepth,
                OutstandingDeliveries = this.OutstandingDeliveries
            };
        }
    }
}
