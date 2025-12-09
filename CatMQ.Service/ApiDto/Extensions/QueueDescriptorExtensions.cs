using NTDLS.CatMQ.Server.Management;

namespace CatMQ.Service.ApiDto.Extensions
{
    public static class QueueDescriptorExtensions
    {
        public static CMqQueueDescriptorDto ToDto(this CMqQueueDescriptor q)
        {
            return new CMqQueueDescriptorDto
            {
                QueueName = q.QueueName,

                DeliveryThrottleMilliseconds = (int?)q.DeliveryThrottle.TotalMilliseconds,
                MaxOutstandingDeliveries = q.MaxOutstandingDeliveries,
                MaxDeliveryAttempts = q.MaxDeliveryAttempts,
                MaxMessageAgeSeconds = (int?)q.MaxMessageAge?.TotalSeconds ?? 0,
                ConsumptionScheme = q.ConsumptionScheme,
                DeliveryScheme = q.DeliveryScheme,
                PersistenceScheme = q.PersistenceScheme,

                QueueDepth = q.QueueDepth,
                CurrentSubscriberCount = q.CurrentSubscriberCount,
                CurrentOutstandingDeliveries = q.CurrentOutstandingDeliveries,

                ReceivedMessageCount = q.ReceivedMessageCount,
                ExpiredMessageCount = q.ExpiredMessageCount,
                DeliveredMessageCount = q.DeliveredMessageCount,
                FailedDeliveryCount = q.FailedDeliveryCount,
                DeferredDeliveryCount = q.DeferredDeliveryCount,
                ExplicitDropCount = q.ExplicitDropCount,
                ExplicitDeadLetterCount = q.ExplicitDeadLetterCount
            };
        }
    }
}
