using NTDLS.ReliableMessaging;

namespace NTDLS.CatMQ.Shared.Payload.ClientToServer
{
    /// <summary>
    /// Represents a query to purge all messages from a specified message queue.
    /// </summary>
    /// <remarks>This query is used to remove all messages from the specified queue. It is typically used in
    /// scenarios where the queue needs to be cleared before processing new messages or when the existing messages are
    /// no longer needed.</remarks>
    /// <param name="queueName">The name of the queue to be purged. Cannot be null or empty.</param>
    public class CMqPurgeQueueQuery(string queueName)
        : IRmQuery<CMqPurgeQueueQueryReply>
    {
        /// <summary>
        /// Gets or sets the name of the queue.
        /// </summary>
        public string QueueName { get; set; } = queueName;
    }

    /// <summary>
    /// Represents the reply to a purge queue query operation, providing information about the success or failure of the
    /// operation.
    /// </summary>
    /// <remarks>This class encapsulates the result of a purge queue query operation, including whether the
    /// operation succeeded and any associated error message. Instances of this class can be initialized with specific
    /// success states or exception details.</remarks>
    public class CMqPurgeQueueQueryReply
        : IRmQueryReply
    {
        /// <summary>
        /// Gets or sets a value indicating whether the operation was successful.
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// Gets or sets the error message associated with the current operation.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="CMqPurgeQueueQueryReply"/> class, representing a failed
        /// operation.
        /// </summary>
        /// <remarks>This constructor sets the <see cref="IsSuccess"/> property to <see langword="false"/>
        /// and populates the <see cref="ErrorMessage"/> property with the message from the provided <paramref
        /// name="exception"/>.</remarks>
        /// <param name="exception">The exception that caused the operation to fail. Cannot be <see langword="null"/>.</param>
        public CMqPurgeQueueQueryReply(Exception exception)
        {
            IsSuccess = false;
            ErrorMessage = exception.Message;
        }

        /// <summary>
        /// Represents the reply to a purge queue query operation.
        /// </summary>
        /// <param name="isSuccess">A value indicating whether the purge queue query operation was successful.</param>
        public CMqPurgeQueueQueryReply(bool isSuccess)
        {
            IsSuccess = isSuccess;
        }

        /// <summary>
        /// Represents a reply to a purge queue query in the messaging system.
        /// </summary>
        /// <remarks>This class is used to encapsulate the response data for a query that purges a queue.
        /// It provides a structured way to handle the reply from the messaging system.</remarks>
        public CMqPurgeQueueQueryReply()
        {
        }
    }
}
