using NTDLS.CatMQ.Shared;

namespace NTDLS.CatMQ.Server.Management
{
    /// <summary>
    /// Defines a queue configuration.
    /// </summary>
    public class CMqQueueDescriptor()
    {
        /// <summary>
        /// The name of the queue.
        /// </summary>
        public string QueueName { get; internal set; } = string.Empty;

        /// <summary>
        /// The amount of time to wait between sending individual messages to subscribers.
        /// </summary>
        public TimeSpan DeliveryThrottle { get; internal set; } = TimeSpan.Zero;

        /// <summary>
        /// The maximum number of times the server will attempt to deliver any message to a subscriber before giving up. 0 = infinite.
        /// </summary>
        public int MaxDeliveryAttempts { get; internal set; } = 10;

        /// <summary>
        /// The maximum time that a message item can remain in the queue without being delivered before being removed. 0 = infinite.
        /// </summary>
        public TimeSpan MaxMessageAge { get; internal set; } = TimeSpan.Zero;

        /// <summary>
        /// Determines when to remove messages from the queue as they are distributed to subscribers.
        /// </summary>
        public CMqConsumptionScheme ConsumptionScheme { get; internal set; } = CMqConsumptionScheme.Delivered;

        /// <summary>
        /// Determines how messages are distributed to subscribers.
        /// </summary>
        public CMqDeliveryScheme DeliveryScheme { get; internal set; } = CMqDeliveryScheme.Balanced;

        /// <summary>
        /// The current number of messages that are enqueued in this message queue.
        /// </summary>
        public int CurrentMessageCount { get; internal set; }

        /// <summary>
        /// The number of subscriber for this queue.
        /// </summary>
        public int CurrentSubscriberCount { get; set; }

        /// <summary>
        /// The total number of messages that have been enqueued into this queue.
        /// </summary>
        public ulong ReceivedMessageCount { get; internal set; }

        /// <summary>
        /// The total number of messages that have been removed from this queue due to age expiration.
        /// </summary>
        public ulong ExpiredMessageCount { get; internal set; }

        /// <summary>
        /// The total number of messages that have been delivered from this queue to subscribers.
        /// </summary>
        public ulong DeliveredMessageCount { get; internal set; }

        /// <summary>
        /// The total number of messages that have failed to deliver from this queue to subscribers.
        /// </summary>
        public ulong DeliveryFailureCount { get; internal set; }

        /// <summary>
        /// Whether the queue is persisted or ephemeral.
        /// </summary>
        public CMqPersistenceScheme PersistenceScheme { get; set; } = CMqPersistenceScheme.Ephemeral;
    }
}
