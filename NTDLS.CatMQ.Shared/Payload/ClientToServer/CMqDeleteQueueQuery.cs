using NTDLS.ReliableMessaging;

namespace NTDLS.CatMQ.Shared.Payload.ClientToServer
{
    /// <summary>
    /// Represents a query to delete a message queue.
    /// </summary>
    /// <remarks>This query is used to request the deletion of a specific message queue identified by its
    /// name. Ensure that the queue name provided exists and is valid within the context of the system.</remarks>
    /// <param name="queueName">The name of the message queue to be deleted. Cannot be null or empty.</param>
    public class CMqDeleteQueueQuery(string queueName)
        : IRmQuery<CMqDeleteQueueQueryReply>
    {
        /// <summary>
        /// Gets or sets the name of the queue.
        /// </summary>
        public string QueueName { get; set; } = queueName;
    }

    /// <summary>
    /// Represents the reply to a delete queue operation, providing information about the success or failure of the
    /// operation.
    /// </summary>
    /// <remarks>This class encapsulates the result of a delete queue operation, including whether the
    /// operation succeeded and any associated error message. Instances of this class can be initialized with success
    /// status or an exception to provide detailed error information.</remarks>
    public class CMqDeleteQueueQueryReply
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
        /// Initializes a new instance of the <see cref="CMqDeleteQueueQueryReply"/> class, representing a failed queue
        /// deletion operation.
        /// </summary>
        /// <remarks>This constructor sets the <see cref="IsSuccess"/> property to <see langword="false"/>
        /// and populates the <see cref="ErrorMessage"/> property with the message from the provided
        /// exception.</remarks>
        /// <param name="exception">The exception that caused the operation to fail. Cannot be <see langword="null"/>.</param>
        public CMqDeleteQueueQueryReply(Exception exception)
        {
            IsSuccess = false;
            ErrorMessage = exception.Message;
        }

        /// <summary>
        /// Represents the reply to a delete queue query operation.
        /// </summary>
        /// <param name="isSuccess">A value indicating whether the delete queue operation was successful. <see langword="true"/> if the
        /// operation succeeded; otherwise, <see langword="false"/>.</param>
        public CMqDeleteQueueQueryReply(bool isSuccess)
        {
            IsSuccess = isSuccess;
        }

        /// <summary>
        /// Represents the reply to a query for deleting a message queue.
        /// </summary>
        /// <remarks>This class is used to encapsulate the response details when a delete queue query is
        /// executed. It does not perform any operations itself but serves as a container for the query reply.</remarks>
        public CMqDeleteQueueQueryReply()
        {
        }

        /// <summary>
        /// Throws an exception if the current operation did not succeed.
        /// </summary>
        /// <remarks>Use this method to enforce that the operation completed successfully. If the
        /// operation failed, an exception is thrown to indicate the error condition.</remarks>
        /// <exception cref="Exception">Thrown if the operation has failed. The exception message contains the associated error message.</exception>
        public void ThrowIfFailed()
        {
            if (!IsSuccess)
            {
                throw new Exception(ErrorMessage);
            }
        }
    }
}
