namespace NTDLS.CatMQ.Client
{
    /// <summary>
    /// Describes a subscription to a queue, this reference is needed to unsubscribe to a single queue subscription.
    /// </summary>
    public class CMqSubscription
    {
        /// <summary>
        /// The unique identifier for this subscription.
        /// </summary>
        public Guid Id { get; private set; } = Guid.NewGuid();

        /// <summary>
        /// The name of the queue that this subscription is for.
        /// </summary>
        public string QueueName { get; internal set; }

        /// <summary>
        /// Function that is called when a message is received for this subscription.
        /// </summary>
        public OnMessageReceived? DeliveryEvent { get; internal set; }

        /// <summary>
        /// For buffered subscriptions, this is the number of messages that the client will attempt to honor.
        /// </summary>
        public int? BatchSize { get; private set; }

        /// <summary>
        /// The interval in which the message buffer will be flushed even if the batch size is not met. 0 = never.
        /// </summary>
        public TimeSpan AutoFlushInterval { get; set; } = TimeSpan.Zero;

        internal DateTime LastBufferFlushed { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Function that is called when a buffered batch is received for this subscription.
        /// </summary>
        public OnBatchReceived? BatchDeliveryEvent { get; internal set; }

        internal CMqSubscription(string queueName, OnMessageReceived deliveryEvent)
        {
            QueueName = queueName;
            DeliveryEvent = deliveryEvent;
        }

        internal CMqSubscription(string queueName, int bufferSize, TimeSpan autoFlushInterval, OnBatchReceived batchDeliveryEvent)
        {
            QueueName = queueName;
            BatchDeliveryEvent = batchDeliveryEvent;
            BatchSize = bufferSize;
            AutoFlushInterval = autoFlushInterval;
        }
    }
}
