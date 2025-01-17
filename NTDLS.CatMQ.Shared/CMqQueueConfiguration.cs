using NTDLS.Helpers;
using System.Text.Json.Serialization;

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
        /// The amount of time to wait between sending individual messages to subscribers.
        /// </summary>
        public TimeSpan DeliveryThrottle { get; set; } = TimeSpan.Zero;

        /// <summary>
        /// Denotes whether this is a dead-letter queue.
        /// </summary>
        public bool IsDeadLetter { get; set; } = false;

        /// <summary>
        /// The maximum number of times the server will attempt to deliver any message to a subscriber before giving up. 0 = infinite.
        /// </summary>
        public int MaxDeliveryAttempts { get; set; } = 10;

        /// <summary>
        /// The maximum time that a message item can remain in the queue without being delivered before being removed. 0 = infinite.
        /// </summary>
        public TimeSpan? MaxMessageAge { get; set; }

        /// <summary>
        /// Determines when to remove messages from the queue as they are distributed to subscribers.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public CMqConsumptionScheme ConsumptionScheme { get; set; } = CMqConsumptionScheme.Delivered;

        /// <summary>
        /// Determines how messages are distributed to subscribers.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public CMqDeliveryScheme DeliveryScheme { get; set; } = CMqDeliveryScheme.Balanced;

        /// <summary>
        /// Whether the queue is persisted or ephemeral.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public CMqPersistenceScheme PersistenceScheme { get; set; } = CMqPersistenceScheme.Ephemeral;

        /// <summary>
        /// Optional configuration for companion queue which contains expired messages or failed deliveries.
        /// </summary>
        public CMqDeadLetterQueueConfiguration? DeadLetterConfiguration { get; set; } = null;

        /// <summary>
        /// Instantiates a new instance of CMqQueueConfiguration.
        /// </summary>
        public CMqQueueConfiguration()
        {
        }

        /// <summary>
        /// Instantiates a new instance of CMqQueueConfiguration.
        /// </summary>
        /// <param name="queueName"></param>
        public CMqQueueConfiguration(string queueName)
        {
            QueueName = queueName;
        }
    }
}
