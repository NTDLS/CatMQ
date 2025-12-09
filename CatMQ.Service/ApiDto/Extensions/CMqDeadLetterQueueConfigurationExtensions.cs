using NTDLS.CatMQ.Shared;

namespace CatMQ.Service.ApiDto.Extensions
{
    public static class CMqDeadLetterQueueConfigurationExtensions
    {
        public static CMqDeadLetterQueueConfiguration? ToDomain(this CMqDeadLetterQueueConfigurationDto? dto)
        {
            if (dto == null)
            {
                return null;
            }

            var defaults = new CMqDeadLetterQueueConfiguration();

            return new CMqDeadLetterQueueConfiguration
            {
                DeliveryThrottle = dto.DeliveryThrottleMilliseconds.HasValue
                    ? TimeSpan.FromMilliseconds(dto.DeliveryThrottleMilliseconds.Value)
                    : defaults.DeliveryThrottle,

                MaxDeliveryAttempts = dto.MaxDeliveryAttempts ?? defaults.MaxDeliveryAttempts,

                MaxMessageAge = dto.MaxMessageAgeSeconds.HasValue
                    ? TimeSpan.FromSeconds(dto.MaxMessageAgeSeconds.Value)
                    : defaults.MaxMessageAge,

                ConsumptionScheme = dto.ConsumptionScheme ?? defaults.ConsumptionScheme,
                DeliveryScheme = dto.DeliveryScheme ?? defaults.DeliveryScheme,
                PersistenceScheme = dto.PersistenceScheme ?? defaults.PersistenceScheme
            };
        }
    }
}
