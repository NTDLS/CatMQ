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
        public ulong DeliveryAttempts { get; internal set; }

        /// <summary>
        /// The number of messages that have been successfully delivered to the subscriber.
        /// </summary>
        public ulong SuccessfulMessagesDeliveries { get; internal set; }

        /// <summary>
        /// The number of messages that failed when attempting to deliver to the subscriber.
        /// </summary>
        public ulong FailedMessagesDeliveries { get; internal set; }

        /// <summary>
        /// The number of messages that have been successfully delivered to and marked as consumed by the subscriber.
        /// </summary>
        public ulong ConsumedMessages { get; internal set; }

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
