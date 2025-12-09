using NTDLS.CatMQ.Shared;

namespace CatMQ.Service.ApiDto
{
    /// <summary>
    /// Public API representation of a dead-letter queue template.
    /// This maps to CMqDeadLetterQueueConfiguration.
    /// The actual DLQ queue name is derived as {parentQueueName}.dlq.
    /// </summary>
    public class CMqDeadLetterQueueConfigurationDto
    {
        public int? DeliveryThrottleMilliseconds { get; set; }   // CMqDeadLetterQueueConfiguration.DeliveryThrottle
        public int? MaxDeliveryAttempts { get; set; }   // CMqDeadLetterQueueConfiguration.MaxDeliveryAttempts
        public int? MaxMessageAgeSeconds { get; set; }   // CMqDeadLetterQueueConfiguration.MaxMessageAge

        public CMqConsumptionScheme? ConsumptionScheme { get; set; }
        public CMqDeliveryScheme? DeliveryScheme { get; set; }
        public CMqPersistenceScheme? PersistenceScheme { get; set; }
    }
}
