using NTDLS.ReliableMessaging;

namespace NTDLS.CatMQ.Shared.Payload.ClientToServer
{
    /// <summary>
    /// Represents a query for creating a message queue with the specified configuration.
    /// </summary>
    /// <remarks>This query is used to define the configuration of a message queue to be created. The
    /// configuration is specified using the <see cref="CMqQueueConfiguration"/> object.</remarks>
    /// <param name="queueConfiguration"></param>
    public class CMqCreateQueueQuery(CMqQueueConfiguration queueConfiguration)
        : IRmQuery<CMqCreateQueueQueryReply>
    {
        /// <summary>
        /// Gets or sets the configuration settings for the message queue.
        /// </summary>
        public CMqQueueConfiguration QueueConfiguration { get; set; } = queueConfiguration;
    }

    /// <summary>
    /// Represents the reply to a queue creation query, indicating success or failure.
    /// </summary>
    /// <remarks>This class provides information about the outcome of a queue creation operation, including
    /// whether the operation  succeeded and, if applicable, an error message describing the failure.</remarks>
    public class CMqCreateQueueQueryReply
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
        /// Initializes a new instance of the <see cref="CMqCreateQueueQueryReply"/> class, representing a failed queue
        /// creation operation.
        /// </summary>
        /// <remarks>This constructor sets the <see cref="IsSuccess"/> property to <see langword="false"/>
        /// and populates the <see cref="ErrorMessage"/> property with the message from the provided <paramref
        /// name="exception"/>.</remarks>
        /// <param name="exception">The exception that caused the failure. Cannot be <see langword="null"/>.</param>
        public CMqCreateQueueQueryReply(Exception exception)
        {
            IsSuccess = false;
            ErrorMessage = exception.Message;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CMqCreateQueueQueryReply"/> class.
        /// </summary>
        /// <param name="isSuccess">A value indicating whether the queue creation query was successful. <see langword="true"/> if the query
        /// succeeded; otherwise, <see langword="false"/>.</param>
        public CMqCreateQueueQueryReply(bool isSuccess)
        {
            IsSuccess = isSuccess;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CMqCreateQueueQueryReply"/> class.
        /// </summary>
        /// <remarks>This constructor creates an instance of the <see cref="CMqCreateQueueQueryReply"/>
        /// class. Use this class to represent the reply to a queue creation query in the messaging system.</remarks>
        public CMqCreateQueueQueryReply()
        {
        }
    }
}
