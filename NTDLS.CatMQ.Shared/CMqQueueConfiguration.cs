using NTDLS.Helpers;

namespace NTDLS.CatMQ.Shared
{
    /// <summary>
    /// Defines a queue configuration.
    /// </summary>
    public class CMqQueueConfiguration
    {
        private string? _queueName;
        /// <summary>
        /// The name of the queue.
        /// </summary>
        public string QueueName
        {
            get => _queueName.EnsureNotNull();
            set => _queueName = value;
        }

        /// <summary>
        /// The interval in which the queue will deliver all of its contents to the subscribers. 0 = immediate.
        /// </summary>
        public TimeSpan BatchDeliveryInterval { get; set; } = TimeSpan.Zero;

        /// <summary>
        /// The amount of time to wait between sending individual messages to subscribers.
        /// </summary>
        public TimeSpan DeliveryThrottle { get; set; } = TimeSpan.Zero;

        /// <summary>
        /// The maximum number of times the server will attempt to deliver any message to a subscriber before giving up. 0 = infinite.
        /// </summary>
        public int MaxDeliveryAttempts { get; set; } = 10;

        /// <summary>
        /// The maximum time that a message item can remain in the queue without being delivered before being removed. 0 = infinite.
        /// </summary>
        public TimeSpan MaxMessageAge { get; set; } = TimeSpan.Zero;

        /// <summary>
        /// Determines when to remove messages from the queue as they are distributed to subscribers.
        /// </summary>
        public PMqConsumptionScheme ConsumptionScheme { get; set; } = PMqConsumptionScheme.SuccessfulDeliveryToAllSubscribers;

        /// <summary>
        /// Determines how messages are distributed to subscribers.
        /// </summary>
        public PMqDeliveryScheme DeliveryScheme { get; set; } = PMqDeliveryScheme.Random;

        /// <summary>
        /// Whether the queue is persisted or ephemeral.
        /// </summary>
        public PMqPersistenceScheme PersistenceScheme { get; set; } = PMqPersistenceScheme.Ephemeral;

        /// <summary>
        /// Instanciates a new instance of CMqQueueConfiguration.
        /// </summary>
        public CMqQueueConfiguration()
        {
        }

        /// <summary>
        /// Instanciates a new instance of CMqQueueConfiguration.
        /// </summary>
        /// <param name="queueName"></param>
        public CMqQueueConfiguration(string queueName)
        {
            QueueName = queueName;
        }
    }
}
