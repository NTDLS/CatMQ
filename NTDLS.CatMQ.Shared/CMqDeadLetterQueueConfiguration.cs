using System.Text.Json.Serialization;

namespace NTDLS.CatMQ.Shared
{
    /// <summary>
    /// Defines a dead-letter queue configuration.
    /// </summary>
    public class CMqDeadLetterQueueConfiguration
    {
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
        public TimeSpan? MaxMessageAge { get; set; }

        /// <summary>
        /// Determines when to remove messages from the queue as they are distributed to subscribers.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public CMqConsumptionScheme ConsumptionScheme { get; set; } = CMqConsumptionScheme.AllSubscribersSatisfied;

        /// <summary>
        /// Determines how messages are distributed to subscribers.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public CMqDeliveryScheme DeliveryScheme { get; set; } = CMqDeliveryScheme.Random;

        /// <summary>
        /// Whether the queue is persisted or ephemeral.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public CMqPersistenceScheme PersistenceScheme { get; set; } = CMqPersistenceScheme.Ephemeral;

        /// <summary>
        /// Instantiates a new instance of CMqQueueConfiguration.
        /// </summary>
        public CMqDeadLetterQueueConfiguration()
        {
        }

        /// <summary>
        /// Converts the current queue settings into a <see cref="CMqQueueConfiguration"/> instance configured as a
        /// dead-letter queue.
        /// </summary>
        /// <remarks>The generated dead-letter queue configuration uses the parent queue name to derive
        /// its name by appending ".dlq". The configuration is tailored for handling undeliverable messages, with
        /// properties such as <see cref="CMqQueueConfiguration.IsDeadLetter"/> set to <see langword="true"/>.</remarks>
        /// <param name="parentQueueName">The name of the parent queue. This value is used to generate the name of the dead-letter queue.</param>
        /// <returns>A <see cref="CMqQueueConfiguration"/> instance representing the dead-letter queue configuration. The
        /// returned configuration includes properties such as <see cref="CMqQueueConfiguration.IsDeadLetter"/>, <see
        /// cref="CMqQueueConfiguration.MaxMessageAge"/>, and <see cref="CMqQueueConfiguration.MaxDeliveryAttempts"/>.</returns>
        public CMqQueueConfiguration ToConfiguration(string parentQueueName)
        {
            return new CMqQueueConfiguration($"{parentQueueName}.dlq")
            {
                IsDeadLetter = true,
                DeadLetterConfiguration = null,
                ConsumptionScheme = ConsumptionScheme,
                MaxMessageAge = MaxMessageAge,
                PersistenceScheme = PersistenceScheme,
                MaxDeliveryAttempts = MaxDeliveryAttempts,
                DeliveryScheme = DeliveryScheme,
                DeliveryThrottle = DeliveryThrottle,
            };
        }
    }
}