using NTDLS.CatMQ.Shared;

namespace NTDLS.CatMQ.Server
{
    /// <summary>
    /// Carries messages from a queue to a consumer. The queue decides which message goes to which consumer and
    /// when, and enforces its limits, so that a transport only has to deliver a message and report what happened to it.
    /// </summary>
    public interface ICMqDeliveryTransport
    {
        /// <summary>
        /// The name of the transport, such as "tcp".
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Whether the consumer can accept a delivery right now. While this is false the consumer is skipped, which does
        /// not count as a delivery attempt, and messages that are waiting for it stay in the queue. Transports where the
        /// consumer asks for messages can use this to only receive deliveries while one of those requests is waiting.
        /// </summary>
        bool IsReady => true;

        /// <summary>
        /// Delivers a message and completes when its outcome is known. The delivery occupies one of the queue's
        /// outstanding delivery slots until then, so this should not complete before the consumer has responded.
        /// Throwing an exception counts as a failed delivery, which may be retried. The transport is responsible for
        /// its own timeouts, because a delivery that never completes holds its slot forever.
        /// </summary>
        /// <param name="message">The message to deliver.</param>
        /// <param name="cancellationToken">Signaled when the queue is shutting down.</param>
        Task<CMqConsumeResult> DeliverAsync(CMqReceivedMessage message, CancellationToken cancellationToken);
    }
}
