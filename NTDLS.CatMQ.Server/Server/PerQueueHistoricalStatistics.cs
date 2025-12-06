using Microsoft.Extensions.Caching.Memory;
using NTDLS.CatMQ.Server.Management;
using NTDLS.Helpers;

namespace NTDLS.CatMQ.Server.Server
{
    /// <summary>
    /// Used to keep a historical record of each queue metrics for the UI charts.
    /// </summary>
    internal class PerQueueHistoricalStatistics
    {
        private int _maxHistoryIntervals = 60;
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());

        public PerQueueHistoricalStatistics(CMqServerConfiguration _configuration)
        {
            _maxHistoryIntervals = _configuration.MaxHistoricalStatisticsDatapoints;
        }

        private Dictionary<DateTime, CMqPerQueueHistoricalStatisticsDescriptor> GetQueueDictionary(string queueName)
        {
            return _cache.GetOrCreate(queueName.ToLowerInvariant(), entry =>
            {
                entry.SlidingExpiration = TimeSpan.FromHours(1);
                return new Dictionary<DateTime, CMqPerQueueHistoricalStatisticsDescriptor>();
            }).EnsureNotNull();
        }

        public void SetDescreteValues(string queueName, CMqQueueDescriptor queueDescriptor)
            => GetCurrentGranularitySlot(queueName).SetDescreteValues(queueDescriptor);

        private CMqPerQueueHistoricalStatisticsDescriptor GetCurrentGranularitySlot(string queueName)
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

            var dictionary = GetQueueDictionary(queueName);

            lock (dictionary)
            {
                // Trim the dictionary to the max size (oldest first).
                if (dictionary.Count > _maxHistoryIntervals)
                {
                    var keysToRemove = dictionary
                        .OrderBy(kvp => kvp.Key)
                        .Take(dictionary.Count - _maxHistoryIntervals)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    foreach (var key in keysToRemove)
                    {
                        dictionary.Remove(key);
                    }
                }

                // Get or create the descriptor for this minute bucket.
                if (!dictionary.TryGetValue(slot, out var descriptor))
                {
                    descriptor = new CMqPerQueueHistoricalStatisticsDescriptor();
                    dictionary[slot] = descriptor;
                }

                return descriptor;
            }
        }

        public Dictionary<DateTime, CMqPerQueueHistoricalStatisticsDescriptor> GetQueueStatistics(string queueName)
        {
            var dictionary = GetQueueDictionary(queueName);

            lock (dictionary)
            {
                // Return a deep-ish clone so callers can't mutate the cached instance.
                return dictionary.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (CMqPerQueueHistoricalStatisticsDescriptor)kvp.Value.Clone()
                );
            }
        }

        public Dictionary<DateTime, CMqPerQueueHistoricalStatisticsDescriptor> GetAllQueueStatistics()
        {
            var result = new Dictionary<DateTime, CMqPerQueueHistoricalStatisticsDescriptor>();

            foreach (var key in _cache.Keys)
            {
                if (_cache.TryGetValue(key, out var cacheItem) && cacheItem is Dictionary<DateTime, CMqPerQueueHistoricalStatisticsDescriptor> dictionary)
                {
                    lock (dictionary)
                    {
                        foreach (var kvp in dictionary)
                        {
                            if (result.TryGetValue(kvp.Key, out var agg))
                            {
                                agg.EnqueuedCount += kvp.Value.EnqueuedCount;
                                agg.DeliveryCount += kvp.Value.DeliveryCount;
                                agg.DequeuedCount += kvp.Value.DequeuedCount;
                                agg.QueueDepth += kvp.Value.QueueDepth;
                                agg.SubscriberCount += kvp.Value.SubscriberCount;
                                agg.OutstandingDeliveries += kvp.Value.OutstandingDeliveries;
                            }
                            else
                            {
                                // start with a clone so modifications don’t affect cache.
                                result[kvp.Key] = (CMqPerQueueHistoricalStatisticsDescriptor)kvp.Value.Clone();
                            }
                        }
                    }
                }
            }

            // Sort result so consumer gets ascending timeline.
            return result
                .OrderBy(kvp => kvp.Key)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        public void IncrementEnqueuedCount(string queueName)
            => GetCurrentGranularitySlot(queueName)?.IncrementEnqueuedCount();

        public void IncrementDeliveryCount(string queueName)
            => GetCurrentGranularitySlot(queueName)?.IncrementDeliveryCount();

        public void IncrementDequeuedCount(string queueName)
            => GetCurrentGranularitySlot(queueName)?.IncrementDequeuedCount();
    }
}
