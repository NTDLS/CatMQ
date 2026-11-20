using NTDLS.CatMQ.Shared;

namespace NTDLS.CatMQ.Server
{
    /// <summary>
    /// A message that has been leased to a consumer. It occupies one of the queue's outstanding delivery
    /// slots until it is acknowledged, and goes back to the queue if the lease expires first.
    /// </summary>
    public class CMqLeasedMessage(string leaseId, DateTime leaseExpiresAt, CMqReceivedMessage message)
    {
        /// <summary>
        /// Identifies the lease when acknowledging or extending it.
        /// </summary>
        public string LeaseId { get; } = leaseId;

        /// <summary>
        /// The UTC date and time when the lease was due to expire at the time that it was received.
        /// </summary>
        public DateTime LeaseExpiresAt { get; } = leaseExpiresAt;

        /// <summary>
        /// The leased message.
        /// </summary>
        public CMqReceivedMessage Message { get; } = message;
    }

    /// <summary>
    /// The outcome of acknowledging or extending a lease.
    /// </summary>
    public enum CMqLeaseStatus
    {
        /// <summary>
        /// The lease is active, which is the result of successfully extending it.
        /// </summary>
        Active,
        /// <summary>
        /// The lease has been acknowledged. Acknowledging a lease again has the same result.
        /// </summary>
        Acknowledged,
        /// <summary>
        /// The lease ran out or was released, so the message may already be with another consumer.
        /// </summary>
        Expired,
        /// <summary>
        /// The lease is not known. It may be from before a restart, or too old to be remembered.
        /// </summary>
        NotFound
    }

    /// <summary>
    /// Thrown by a transport when it cannot take a delivery after all. The message stays in
    /// the queue, and this does not count as a delivery attempt or as a failed delivery.
    /// </summary>
    public class CMqDeliveryNotAcceptedException(string message)
        : Exception(message)
    {
    }
}
