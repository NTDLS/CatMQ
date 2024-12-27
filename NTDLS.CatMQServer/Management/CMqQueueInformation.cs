using NTDLS.CatMQShared;

namespace NTDLS.CatMQServer.Management
{
    /// <summary>
    /// Defines a queue configuration.
    /// </summary>
    public class CMqQueueInformation()
    {
        /// <summary>
        /// The name of the queue.
        /// </summary>
        public string QueueName { get; internal set; } = string.Empty;

        /// <summary>
        /// The interval in which the queue will deliver all of its contents to the subscribers. 0 = immediate.
        /// </summary>
        public TimeSpan BatchDeliveryInterval { get; internal set; } = TimeSpan.Zero;

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
        public PMqConsumptionScheme ConsumptionScheme { get; internal set; } = PMqConsumptionScheme.SuccessfulDeliveryToAllSubscribers;

        /// <summary>
        /// Determines how messages are distributed to subscribers.
        /// </summary>
        public PMqDeliveryScheme DeliveryScheme { get; internal set; } = PMqDeliveryScheme.Random;

        /// <summary>
        /// The current number of messages that are enqueued in this message queue.
        /// </summary>
        public int CurrentEnqueuedMessageCount { get; internal set; }

        /// <summary>
        /// The number of subscriber for this queue.
        /// </summary>
        public int SubscriberCount { get; set; }

        /// <summary>
        /// The current number of messages that are enqueued in this message queue.
        /// </summary>
        public ulong TotalDeliveredMessages { get; internal set; }

        /// <summary>
        /// The total number of messages that have been enqueued to this message queue.
        /// </summary>
        public ulong TotalEnqueuedMessages { get; internal set; }

        /// <summary>
        /// The total number of messages that have expired in this message queue without being delivered.
        /// </summary>
        public ulong TotalExpiredMessages { get; internal set; }

        /// <summary>
        /// Whether the queue is persisted or ephemeral.
        /// </summary>
        public PMqPersistence Persistence { get; set; } = PMqPersistence.Ephemeral;
    }
}
