using System.Security.Cryptography;
using System.Text;

namespace NTDLS.CatMQ.Server.Management
{
    /// <summary>
    /// Contains information about queue subscribers.
    /// </summary>
    public class CMqSubscriberDescriptor
    {
        internal CMqSubscriberDescriptor(Guid subscriberId, ICMqDeliveryTransport transport, string? consumerName = null)
        {
            SubscriberId = subscriberId;
            Transport = transport;
            ConsumerName = consumerName;
            ConsumerId = consumerName == null ? subscriberId : new Guid(MD5.HashData(Encoding.UTF8.GetBytes(consumerName)));
        }

        /// <summary>
        /// Carries messages to this subscriber.
        /// </summary>
        internal ICMqDeliveryTransport Transport { get; }

        /// <summary>
        /// The name of the transport that carries messages to this subscriber, such as "tcp".
        /// </summary>
        public string TransportName => Transport.Name;

        /// <summary>
        /// The name that the consumer chose. Null when it is identified only by its connection.
        /// </summary>
        public string? ConsumerName { get; internal set; }

        /// <summary>
        /// The identity used to track which consumers have received a message. It is derived from ConsumerName
        /// so that it is the same across connections, and otherwise is the same as SubscriberId.
        /// </summary>
        public Guid ConsumerId { get; internal set; }

        /// <summary>
        /// The unique id of the subscriber. For a subscriber connected with the native TCP protocol this is its ConnectionId.
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
