using NTDLS.ReliableMessaging;

namespace NTDLS.CatMQ.Shared.Payload.ServerToClient
{
    /// <summary>
    /// Represents a query for delivering a message from a queue, including metadata about the message and its delivery
    /// status.
    /// </summary>
    /// <remarks>This class encapsulates information about a message being delivered from a queue, such as its
    /// unique identifier,  serialized content, type information, and delivery statistics. It is used to track the
    /// delivery progress and manage  deferred delivery requests.</remarks>
    public class CMqMessageDeliveryQuery(string queueName, ulong serialNumber, string assemblyQualifiedTypeName, string messageJson)
    : IRmQuery<CMqMessageDeliveryQueryReply>
    {
        /// <summary>
        /// The name of the queue from which this message is being delivered.
        /// </summary>
        public string QueueName { get; set; } = queueName;

        /// <summary>
        /// The full assembly qualified name of the type of MessageJson.
        /// </summary>
        public string AssemblyQualifiedTypeName { get; set; } = assemblyQualifiedTypeName;

        /// <summary>
        /// Json serialized object of type denoted by ObjectType.
        /// </summary>
        public string MessageJson { get; set; } = messageJson;

        /// <summary>
        /// The unique ID of the message per queue.
        /// </summary>
        public ulong SerialNumber { get; set; } = serialNumber;

        /// <summary>
        /// The UTC date and time when the message was enqueued.
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// The number of times this message has been delivered and the subscriber requested that delivery be deferred.
        /// </summary>
        public int DeferredCount { get; set; }

        /// <summary>
        /// The amount of time to wait before delivering this message.
        /// </summary>
        public TimeSpan? DeferDuration { get; set; }

        /// <summary>
        /// The number of subscribers that this message has been successfully delivered to.
        /// </summary>
        public int SubscriberDeliveryCount { get; set; } = new();

        /// <summary>
        /// The number of subscribers that have responded to the delivery of this message.
        /// </summary>
        public int SatisfiedSubscriberCount { get; set; } = new();

        /// <summary>
        /// The number of subscribers deliveries that have failed.
        /// </summary>
        public int FailedSubscriberCount { get; set; } = new();
    }

    /// <summary>
    /// Represents the reply to a message delivery query, providing information about the consumption status of the
    /// message and any associated error details.
    /// </summary>
    /// <remarks>This class is used to encapsulate the result of a message delivery query, including whether
    /// the message was consumed by the client and any error messages that occurred during processing. The message
    /// remains at the top of the queue until it is consumed by all subscribers, expires, or is dead-lettered.</remarks>
    public class CMqMessageDeliveryQueryReply
        : IRmQueryReply
    {
        /// <summary>
        /// Denotes whether the client which received the delivery notification consumed
        /// the message. The message will not leave the top of the queue until it has been
        /// consumed by all subscribers (or the message expires or is dead-lettered).
        /// </summary>
        public CMqConsumeResult ConsumeResult { get; set; }

        /// <summary>
        /// Gets or sets the error message associated with the current operation.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="CMqMessageDeliveryQueryReply"/> class,  representing a reply to
        /// a message delivery query with an error condition.
        /// </summary>
        /// <param name="exception">The exception that caused the message delivery query to fail.  The exception's message is used to populate
        /// the <see cref="ErrorMessage"/> property.</param>
        public CMqMessageDeliveryQueryReply(Exception exception)
        {
            ConsumeResult = new CMqConsumeResult(CMqConsumptionDisposition.Exception);
            ErrorMessage = exception.Message;
        }

        /// <summary>
        /// Represents a reply to a message delivery query, containing the result of a message consumption operation.
        /// </summary>
        /// <param name="consumeResult">The result of the message consumption operation. Cannot be null.</param>
        public CMqMessageDeliveryQueryReply(CMqConsumeResult consumeResult)
        {
            ConsumeResult = consumeResult;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CMqMessageDeliveryQueryReply"/> class.
        /// </summary>
        /// <remarks>This constructor sets the <see cref="ConsumeResult"/> property to a default value
        /// indicating that the message has not been consumed.</remarks>
        public CMqMessageDeliveryQueryReply()
        {
            ConsumeResult = new CMqConsumeResult(CMqConsumptionDisposition.NotInterested);
        }
    }
}
