namespace NTDLS.CatMQ.Server.Management
{
    /// <summary>
    /// Contains information about queue subscribers.
    /// </summary>
    public class CMqSubscriberDescriptor
    {
        internal CMqSubscriberDescriptor(Guid subscriberId)
        {
            SubscriberId = subscriberId;
        }

        /// <summary>
        /// The unique id of the subscriber, synonymous with ConnectionId.
        /// </summary>
        public Guid SubscriberId { get; internal set; }

        /// <summary>
        /// The number of messages that have been attempted to the subscriber.
        /// </summary>
        public ulong AttemptedDeliveryCount { get => _attemptedDeliveryCount; }
        private ulong _attemptedDeliveryCount = 0;
        internal void IncrementAttemptedDeliveryCount()
            => Interlocked.Increment(ref _attemptedDeliveryCount);

        /// <summary>
        /// The number of messages that have been successfully delivered to the subscriber.
        /// </summary>
        public ulong SuccessfulDeliveryCount { get => _successfulDeliveryCount; }

        private ulong _successfulDeliveryCount = 0;
        internal void IncrementSuccessfulDeliveryCount()
            => Interlocked.Increment(ref _successfulDeliveryCount);

        /// <summary>
        /// The number of messages that failed when attempting to deliver to the subscriber.
        /// </summary>
        public ulong FailedDeliveryCount { get => _failedDeliveryCount; }
        private ulong _failedDeliveryCount = 0;
        internal void IncrementFailedDeliveryCount()
            => Interlocked.Increment(ref _failedDeliveryCount);

        /// <summary>
        /// The total number messages deliveries that have been attempted but were requested to be deferred to a later time.
        /// </summary>
        public ulong DeferredDeliveryCount { get => _deferredDeliveryCount; }
        private ulong _deferredDeliveryCount = 0;
        internal void IncrementDeferredDeliveryCount()
            => Interlocked.Increment(ref _deferredDeliveryCount);

        /// <summary>
        /// The number of messages that have been successfully delivered to and marked as consumed by the subscriber.
        /// </summary>
        public ulong ConsumedDeliveryCount { get => _consumedDeliveryCount; }
        private ulong _consumedDeliveryCount = 0;
        internal void IncrementConsumedDeliveryCount()
            => Interlocked.Increment(ref _consumedDeliveryCount);

        /// <summary>
        /// The remote address of the connected client.
        /// </summary>
        public string? RemoteAddress { get; internal set; }
        /// <summary>
        /// The remote port of the connected client.
        /// </summary>
        public int? RemotePort { get; internal set; }

        /// <summary>
        /// The local address of the connected client.
        /// </summary>
        public string? LocalAddress { get; internal set; }

        /// <summary>
        /// The port address of the connected client.
        /// </summary>
        public int? LocalPort { get; internal set; }
    }
}
