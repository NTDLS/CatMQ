using Microsoft.Extensions.Caching.Memory;
using NTDLS.CatMQ.Server.Management;

namespace NTDLS.CatMQ.Server.Server
{
    /// <summary>
    /// Used to keep a historical record of each queue metrics for the UI charts.
    /// </summary>
    internal static class HistoricalStatistics
    {
        private const int maxHistoryIntervals = 60;
        private static readonly MemoryCache _cache = new(new MemoryCacheOptions());

        private static Dictionary<DateTime, CMqQueueHistoricalStatisticsDescriptor> GetQueueDictionary(string queueName)
        {
            // GetOrCreate guarantees this returns a non-null dictionary for the key.
            return _cache.GetOrCreate(queueName, entry =>
            {
                entry.SlidingExpiration = TimeSpan.FromHours(1);
                return new Dictionary<DateTime, CMqQueueHistoricalStatisticsDescriptor>();
            })!;
        }

        private static CMqQueueHistoricalStatisticsDescriptor? GetCurrentGranularitySlot(string queueName)
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
            if (dictionary == null)
            {
                return null;
            }

            lock (dictionary)
            {
                // Trim the dictionary to the max size (oldest first).
                if (dictionary.Count > maxHistoryIntervals)
                {
                    var keysToRemove = dictionary
                        .OrderBy(kvp => kvp.Key)
                        .Take(dictionary.Count - maxHistoryIntervals)
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
                    descriptor = new CMqQueueHistoricalStatisticsDescriptor();
                    dictionary[slot] = descriptor;
                }

                return descriptor;
            }
        }

        public static Dictionary<DateTime, CMqQueueHistoricalStatisticsDescriptor>? GetQueueStatistics(string queueName)
        {
            var dictionary = GetQueueDictionary(queueName);
            if (dictionary == null)
            {
                return null;
            }

            lock (dictionary)
            {
                // Return a deep-ish clone so callers can't mutate the cached instance.
                return dictionary.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (CMqQueueHistoricalStatisticsDescriptor)kvp.Value.Clone()
                );
            }
        }

        public static Dictionary<DateTime, CMqQueueHistoricalStatisticsDescriptor> GetAllQueueStatistics()
        {
            var result = new Dictionary<DateTime, CMqQueueHistoricalStatisticsDescriptor>();

            foreach (var key in _cache.Keys)
            {
                if (_cache.TryGetValue(key, out var cacheItem) && cacheItem is Dictionary<DateTime, CMqQueueHistoricalStatisticsDescriptor> dictionary)
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
                            }
                            else
                            {
                                // start with a clone so modifications don’t affect cache.
                                result[kvp.Key] = (CMqQueueHistoricalStatisticsDescriptor)kvp.Value.Clone();
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


        public static void IncrementEnqueuedCount(string queueName)
            => GetCurrentGranularitySlot(queueName)?.IncrementEnqueuedCount();

        public static void IncrementDeliveryCount(string queueName)
            => GetCurrentGranularitySlot(queueName)?.IncrementDeliveryCount();

        public static void IncrementDequeuedCount(string queueName)
            => GetCurrentGranularitySlot(queueName)?.IncrementDequeuedCount();
    }
}
