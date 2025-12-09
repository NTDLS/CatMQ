using NTDLS.CatMQ.Shared;

namespace CatMQ.Service.ApiDto.Extensions
{
    public static class CMqQueueConfigurationExtensions
    {
        public static CMqQueueConfiguration ToDomain(this CMqQueueConfigurationDto dto)
        {
            ArgumentNullException.ThrowIfNull(dto);
            ArgumentNullException.ThrowIfNullOrWhiteSpace(dto.QueueName);

            var defaults = new CMqQueueConfiguration();

            var config = new CMqQueueConfiguration(dto.QueueName)
            {
                AsycnronousDelivery = dto.AsynchronousDelivery ?? defaults.AsycnronousDelivery,
                MaxOutstandingDeliveries = dto.MaxOutstandingDeliveries ?? defaults.MaxOutstandingDeliveries,

                DeliveryThrottle = dto.DeliveryThrottleMilliseconds.HasValue
                    ? TimeSpan.FromMilliseconds(dto.DeliveryThrottleMilliseconds.Value)
                    : defaults.DeliveryThrottle,

                IsDeadLetter = dto.IsDeadLetter ?? defaults.IsDeadLetter,
                MaxDeliveryAttempts = dto.MaxDeliveryAttempts ?? defaults.MaxDeliveryAttempts,

                MaxMessageAge = dto.MaxMessageAgeSeconds.HasValue
                    ? TimeSpan.FromSeconds(dto.MaxMessageAgeSeconds.Value)
                    : defaults.MaxMessageAge,

                ConsumptionScheme = dto.ConsumptionScheme ?? defaults.ConsumptionScheme,
                DeliveryScheme = dto.DeliveryScheme ?? defaults.DeliveryScheme,
                PersistenceScheme = dto.PersistenceScheme ?? defaults.PersistenceScheme,

                DeadLetterConfiguration = dto.DeadLetter.ToDomain()
            };

            return config;
        }
    }
}
