using NTDLS.CatMQ.Server.Management;
using NTDLS.Helpers;
using NTDLS.Semaphore;

namespace NTDLS.CatMQ.Server.Server
{
    /// <summary>
    /// Used to keep a historical record of each queue metrics for the UI charts.
    /// </summary>
    internal class HistoricalStatistics
    {
        DeltaContainer<double> _deltaContainer = new(TimeSpan.FromHours(1));
        private readonly int _maxHistoryIntervals = 60;

        private PessimisticCriticalResource<Dictionary<DateTime, CMqPerQueueHistoricalStatisticsDescriptor>> _slots = new();

        public HistoricalStatistics(CMqServerConfiguration _configuration)
        {
            _maxHistoryIntervals = _configuration.MaxHistoricalStatisticsDatapoints;
        }

        public void UpdateCurrentStatistics(MessageQueueStatistics statistics)
        {
            var utcNow = DateTime.UtcNow;

            int bucketSeconds = (utcNow.Second / 10) * 10;

            var slot = new DateTime(
                utcNow.Year,
                utcNow.Month,
                utcNow.Day,
                utcNow.Hour,
                utcNow.Minute,
                bucketSeconds,
                DateTimeKind.Utc);

            var now = DateTime.UtcNow;

            _slots.Use(o =>
            {
                // Trim the dictionary to the max size (oldest first).
                if (o.Count > _maxHistoryIntervals)
                {
                    var keysToRemove = o
                        .OrderBy(kvp => kvp.Key)
                        .Take(o.Count - _maxHistoryIntervals)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    foreach (var key in keysToRemove)
                    {
                        o.Remove(key);
                    }
                }

                _deltaContainer.TryCalculatePerSecond("ReceivedMessageCount", now, statistics.ReceivedMessageCount, out var receiveRate);
                _deltaContainer.TryCalculatePerSecond("DeliveredMessageCount", now, statistics.DeliveredMessageCount, out var deliveryRate);
                _deltaContainer.TryCalculatePerSecond("ExpiredMessageCount", now, statistics.ExpiredMessageCount, out var expiracyRate);
                _deltaContainer.TryCalculatePerSecond("FailedDeliveryCount", now, statistics.FailedDeliveryCount, out var failedDeliveryRate);
                _deltaContainer.TryCalculatePerSecond("DeferredDeliveryCount", now, statistics.DeferredDeliveryCount, out var deferredDeliveryRate);
                _deltaContainer.TryCalculatePerSecond("ExplicitDeadLetterCount", now, statistics.ExplicitDeadLetterCount, out var explicitDeadLetterRate);
                _deltaContainer.TryCalculatePerSecond("ExplicitDropCount", now, statistics.ExplicitDropCount, out var explicitDropRate);

                o[slot] = new CMqPerQueueHistoricalStatisticsDescriptor()
                {
                    //Rate counters:
                    ReceiveRate = receiveRate ?? 0,
                    DeliveryRate = deliveryRate ?? 0,
                    ExpiracyRate = expiracyRate ?? 0,
                    FailedDeliveryRate = failedDeliveryRate ?? 0,
                    DeferredDeliveryRate = deferredDeliveryRate ?? 0,
                    ExplicitDeadLetterRate = explicitDeadLetterRate ?? 0,
                    ExplicitDropRate = explicitDropRate ?? 0,

                    //Descrete counters:
                    OutstandingDeliveries = statistics.OutstandingDeliveries,
                    QueueDepth = statistics.QueueDepth,
                };
            });
        }

        public Dictionary<DateTime, CMqPerQueueHistoricalStatisticsDescriptor> GetQueueStatistics()
            => _slots.Use(o => o.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
    }
}
