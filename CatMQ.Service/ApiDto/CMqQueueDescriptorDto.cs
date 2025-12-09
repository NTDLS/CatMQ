using NTDLS.CatMQ.Shared;

namespace CatMQ.Service.ApiDto
{
    public class CMqQueueDescriptorDto
    {
        public string QueueName { get; set; } = string.Empty;

        // Config-related fields
        public int? DeliveryThrottleMilliseconds { get; set; }
        public int MaxOutstandingDeliveries { get; set; }
        public int MaxDeliveryAttempts { get; set; }
        public int? MaxMessageAgeSeconds { get; set; }

        public CMqConsumptionScheme ConsumptionScheme { get; set; }
        public CMqDeliveryScheme DeliveryScheme { get; set; }
        public CMqPersistenceScheme PersistenceScheme { get; set; }

        // Runtime state
        public int QueueDepth { get; set; }
        public int CurrentSubscriberCount { get; set; }
        public int CurrentOutstandingDeliveries { get; set; }

        public ulong ReceivedMessageCount { get; set; }
        public ulong ExpiredMessageCount { get; set; }
        public ulong DeliveredMessageCount { get; set; }
        public ulong FailedDeliveryCount { get; set; }
        public ulong DeferredDeliveryCount { get; set; }
        public ulong ExplicitDropCount { get; set; }
        public ulong ExplicitDeadLetterCount { get; set; }
    }
}
