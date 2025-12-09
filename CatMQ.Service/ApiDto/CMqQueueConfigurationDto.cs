using NTDLS.CatMQ.Shared;

namespace CatMQ.Service.ApiDto
{
    /// <summary>
    /// Public API representation of a queue configuration.
    /// </summary>
    public class CMqQueueConfigurationDto
    {
        /// <summary>
        /// Name of the queue (required).
        /// </summary>
        public string QueueName { get; set; } = string.Empty;

        // These all map to CMqQueueConfiguration, but are optional so
        // server defaults apply when omitted.

        public bool? AsynchronousDelivery { get; set; }          // CMqQueueConfiguration.AsycnronousDelivery
        public int? MaxOutstandingDeliveries { get; set; }       // CMqQueueConfiguration.MaxOutstandingDeliveries
        public int? DeliveryThrottleMilliseconds { get; set; }   // CMqQueueConfiguration.DeliveryThrottle
        public bool? IsDeadLetter { get; set; }                  // CMqQueueConfiguration.IsDeadLetter
        public int? MaxDeliveryAttempts { get; set; }            // CMqQueueConfiguration.MaxDeliveryAttempts
        public int? MaxMessageAgeSeconds { get; set; }           // CMqQueueConfiguration.MaxMessageAge

        public CMqConsumptionScheme? ConsumptionScheme { get; set; }
        public CMqDeliveryScheme? DeliveryScheme { get; set; }
        public CMqPersistenceScheme? PersistenceScheme { get; set; }

        /// <summary>
        /// Optional dead-letter configuration. If null, no DLQ is created.
        /// </summary>
        public CMqDeadLetterQueueConfigurationDto? DeadLetter { get; set; }
    }
}
