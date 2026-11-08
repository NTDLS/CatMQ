using NTDLS.CatMQ.Shared;
using System.Diagnostics;

namespace NTDLS.CatMQ.Server.Server
{
    /// <summary>
    /// Delivers messages to a named consumer that asks for them. The consumer is ready only while a receive request
    /// is waiting for messages, and each delivery stays open until its lease is acknowledged or ends some other way.
    /// </summary>
    internal class LeaseDeliveryTransport(LeaseManager leases, Action wakeDeliveryThread)
        : ICMqDeliveryTransport
    {
        /// <summary>
        /// Time for the delivery thread to see a receive request, so that a request that does not wait can still get a message.
        /// </summary>
        private static readonly TimeSpan MINIMUM_WAIT = TimeSpan.FromMilliseconds(50);

        /// <summary>
        /// How long to collect more messages for a batch after the first one has arrived.
        /// </summary>
        private static readonly long BATCH_WINDOW_TICKS = Stopwatch.Frequency / 1000; //One millisecond.

        private class Waiter(int max, TimeSpan leaseDuration)
        {
            public int Max { get; } = max;
            public TimeSpan LeaseDuration { get; } = leaseDuration;
            public List<(Lease Lease, CMqLeasedMessage Message)> Received { get; } = new();
            public TaskCompletionSource FirstMessage { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public bool HasCapacity => Received.Count < Max;
        }

        private readonly object _lock = new();
        private readonly List<Waiter> _waiters = new();
        private bool _isClosed;

        /// <summary>
        /// The ID that this consumer is subscribed to its queue with.
        /// </summary>
        public Guid SubscriberId { get; } = Guid.NewGuid();

        public string Name => "lease";

        public bool IsReady
        {
            get
            {
                lock (_lock)
                {
                    return _waiters.Exists(o => o.HasCapacity);
                }
            }
        }

        public async Task<CMqConsumeResult> DeliverAsync(CMqReceivedMessage message, CancellationToken cancellationToken)
        {
            Lease lease;

            lock (_lock)
            {
                //The request that made us ready may have timed out since the message was selected.
                var waiter = _waiters.Find(o => o.HasCapacity)
                    ?? throw new CMqDeliveryNotAcceptedException("There is no receive request waiting for a message.");

                lease = leases.Create(this, waiter.LeaseDuration);
                waiter.Received.Add((lease, new CMqLeasedMessage(lease.Id, lease.ExpiresAt, message)));
                waiter.FirstMessage.TrySetResult();
            }

            using var registration = cancellationToken.Register(lease.Release);
            return await lease.Outcome;
        }

        /// <summary>
        /// Waits for messages. Returns as soon as there are some, or an empty list when the wait is over.
        /// </summary>
        public async Task<List<CMqLeasedMessage>> ReceiveAsync(int max, TimeSpan wait, TimeSpan leaseDuration, CancellationToken cancellationToken)
        {
            var waiter = new Waiter(max, leaseDuration);

            lock (_lock)
            {
                if (_isClosed)
                {
                    return [];
                }
                _waiters.Add(waiter);
            }

            wakeDeliveryThread();

            try
            {
                try
                {
                    await waiter.FirstMessage.Task.WaitAsync(wait < MINIMUM_WAIT ? MINIMUM_WAIT : wait, cancellationToken);
                    await CollectBatchAsync(waiter);
                }
                catch (TimeoutException)
                {
                    //Nothing arrived, which is a normal outcome.
                }

                lock (_lock)
                {
                    _waiters.Remove(waiter);
                    return waiter.Received.Select(o => o.Message).ToList();
                }
            }
            catch
            {
                //Nobody is going to receive these, so they go straight back to the queue.
                List<Lease> abandoned;
                lock (_lock)
                {
                    _waiters.Remove(waiter);
                    abandoned = waiter.Received.Select(o => o.Lease).ToList();
                }
                abandoned.ForEach(o => o.Release());
                throw;
            }
        }

        /// <summary>
        /// Gives the delivery thread a moment to add more messages to a batch that has started. This spins rather than
        /// waiting on a timer, because a timer wait is at least one tick, about 15ms on Windows, which would cap a consumer
        /// at one batch per tick. A batch that is already full returns at once.
        /// </summary>
        private Task CollectBatchAsync(Waiter waiter)
        {
            bool IsFull()
            {
                lock (_lock)
                {
                    return waiter.HasCapacity == false;
                }
            }

            if (IsFull())
            {
                return Task.CompletedTask;
            }

            return Task.Run(() =>
            {
                var until = Stopwatch.GetTimestamp() + BATCH_WINDOW_TICKS;
                while (IsFull() == false && Stopwatch.GetTimestamp() < until)
                {
                    Thread.SpinWait(50);
                }
            });
        }

        /// <summary>
        /// Ends any waiting requests, and stops accepting new ones.
        /// </summary>
        public void Close()
        {
            lock (_lock)
            {
                _isClosed = true;
                foreach (var waiter in _waiters)
                {
                    waiter.FirstMessage.TrySetResult();
                }
            }
        }
    }
}
