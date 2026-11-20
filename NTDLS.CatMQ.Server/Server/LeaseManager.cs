using NTDLS.CatMQ.Shared;
using System.Collections.Concurrent;

namespace NTDLS.CatMQ.Server.Server
{
    /// <summary>
    /// A message on loan to a consumer. Outcome stays pending until the lease is acknowledged, expires or is released,
    /// which is what holds the delivery open. Leases are not persisted: after a restart the message is simply redelivered.
    /// </summary>
    internal class Lease(object owner, TimeSpan duration)
    {
        private enum LeaseState { Active, Acknowledged, Expired }

        private readonly object _lock = new();
        private LeaseState _state = LeaseState.Active;
        private long _expiresAtTicks = Environment.TickCount64 + (long)duration.TotalMilliseconds;
        private long _finishedAtTicks;

        public string Id { get; } = Guid.NewGuid().ToString("N");
        public object Owner { get; } = owner;
        public DateTime ExpiresAt { get; private set; } = DateTime.UtcNow + duration;

        private readonly TaskCompletionSource<CMqConsumeResult> _outcome = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<CMqConsumeResult> Outcome => _outcome.Task;

        private void Finish(LeaseState state)
        {
            _state = state;
            _finishedAtTicks = Environment.TickCount64;
        }

        public CMqLeaseStatus Acknowledge(CMqConsumeResult result)
        {
            lock (_lock)
            {
                if (_state == LeaseState.Active)
                {
                    Finish(LeaseState.Acknowledged);
                    _outcome.TrySetResult(result);
                }
                return _state == LeaseState.Acknowledged ? CMqLeaseStatus.Acknowledged : CMqLeaseStatus.Expired;
            }
        }

        public CMqLeaseStatus Extend(TimeSpan duration, out DateTime expiresAt)
        {
            lock (_lock)
            {
                if (_state == LeaseState.Active)
                {
                    _expiresAtTicks = Environment.TickCount64 + (long)duration.TotalMilliseconds;
                    ExpiresAt = DateTime.UtcNow + duration;
                }
                expiresAt = ExpiresAt;
                return _state switch
                {
                    LeaseState.Active => CMqLeaseStatus.Active,
                    LeaseState.Acknowledged => CMqLeaseStatus.Acknowledged,
                    _ => CMqLeaseStatus.Expired
                };
            }
        }

        /// <summary>
        /// Hands the message back without penalty, because the consumer never had a chance to work on it.
        /// </summary>
        public void Release()
        {
            lock (_lock)
            {
                if (_state == LeaseState.Active)
                {
                    Finish(LeaseState.Expired);
                    _outcome.TrySetException(new CMqDeliveryNotAcceptedException("The lease was released."));
                }
            }
        }

        /// <summary>
        /// Ends the lease if it has run out. This counts as a failed delivery, because the consumer had the message and did not respond.
        /// </summary>
        public void ExpireIfDue(long nowTicks)
        {
            lock (_lock)
            {
                if (_state == LeaseState.Active && nowTicks >= _expiresAtTicks)
                {
                    Finish(LeaseState.Expired);
                    _outcome.TrySetException(new TimeoutException("The lease expired before it was acknowledged."));
                }
            }
        }

        public bool IsFinishedBefore(long ticks)
        {
            lock (_lock)
            {
                return _state != LeaseState.Active && _finishedAtTicks < ticks;
            }
        }
    }

    /// <summary>
    /// Tracks every lease on the server, because leases are acknowledged by their ID alone.
    /// </summary>
    internal class LeaseManager
    {
        /// <summary>
        /// How long a finished lease is remembered, so that a repeated acknowledgment gets the same answer rather than "not found".
        /// </summary>
        private const long FINISHED_LEASE_RETENTION_MS = 60 * 1000;

        private readonly ConcurrentDictionary<string, Lease> _leases = new();

        public Lease Create(object owner, TimeSpan duration)
        {
            var lease = new Lease(owner, duration);
            _leases[lease.Id] = lease;
            return lease;
        }

        public CMqLeaseStatus Acknowledge(string leaseId, CMqConsumeResult result)
            => _leases.TryGetValue(leaseId, out var lease) ? lease.Acknowledge(result) : CMqLeaseStatus.NotFound;

        public CMqLeaseStatus Extend(string leaseId, TimeSpan duration, out DateTime expiresAt)
        {
            if (_leases.TryGetValue(leaseId, out var lease))
            {
                return lease.Extend(duration, out expiresAt);
            }
            expiresAt = default;
            return CMqLeaseStatus.NotFound;
        }

        public void ReleaseOwnedBy(object owner)
        {
            foreach (var lease in _leases.Values.Where(o => ReferenceEquals(o.Owner, owner)))
            {
                lease.Release();
            }
        }

        /// <summary>
        /// Expires leases that have run out, and forgets finished ones that are old enough.
        /// </summary>
        public void Sweep()
        {
            long now = Environment.TickCount64;
            long forgetBefore = now - FINISHED_LEASE_RETENTION_MS;

            foreach (var lease in _leases.Values)
            {
                lease.ExpireIfDue(now);
                if (lease.IsFinishedBefore(forgetBefore))
                {
                    _leases.TryRemove(lease.Id, out _);
                }
            }
        }
    }
}
