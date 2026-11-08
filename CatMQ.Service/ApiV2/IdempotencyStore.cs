using System.Collections.Concurrent;

namespace CatMQ.Service.ApiV2
{
    /// <summary>
    /// Remembers publishes that supplied an idempotency key, so that a publisher can safely retry after losing a response.
    /// Keys are held in memory, so they do not survive a restart.
    /// </summary>
    public class IdempotencyStore
    {
        private static readonly TimeSpan RETENTION = TimeSpan.FromHours(24);
        private const int SWEEP_INTERVAL = 1024;

        private record Entry(Lazy<Guid> MessageId, DateTime ExpiresAt);

        private readonly ConcurrentDictionary<string, Entry> _entries = new();
        private int _addsSinceSweep;

        /// <summary>
        /// Publishes a message unless this key has already been used. Concurrent requests with the same key publish once.
        /// </summary>
        /// <returns>The ID of the message, and whether this was a repeat of an earlier publish.</returns>
        public (Guid MessageId, bool IsDuplicate) Publish(string queueName, string key, Func<Guid> publish)
        {
            var entryKey = $"{queueName.ToLowerInvariant()}\n{key}";
            var now = DateTime.UtcNow;

            if (_entries.TryGetValue(entryKey, out var existing) && existing.ExpiresAt <= now)
            {
                _entries.TryRemove(new KeyValuePair<string, Entry>(entryKey, existing));
            }

            var created = new Entry(new Lazy<Guid>(publish), now + RETENTION);
            var entry = _entries.GetOrAdd(entryKey, created);

            try
            {
                var messageId = entry.MessageId.Value;

                if (ReferenceEquals(entry, created) && Interlocked.Increment(ref _addsSinceSweep) >= SWEEP_INTERVAL)
                {
                    Sweep(now);
                }

                return (messageId, ReferenceEquals(entry, created) == false);
            }
            catch
            {
                //A failed publish must not stop the retry that the key exists for.
                _entries.TryRemove(new KeyValuePair<string, Entry>(entryKey, entry));
                throw;
            }
        }

        private void Sweep(DateTime now)
        {
            Interlocked.Exchange(ref _addsSinceSweep, 0);

            foreach (var entry in _entries.Where(o => o.Value.ExpiresAt <= now))
            {
                _entries.TryRemove(entry);
            }
        }
    }
}
