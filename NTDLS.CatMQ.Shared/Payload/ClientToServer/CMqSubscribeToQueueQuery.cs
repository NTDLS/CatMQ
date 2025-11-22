using NTDLS.ReliableMessaging;

namespace NTDLS.CatMQ.Shared.Payload.ClientToServer
{
    /// <summary>
    /// Represents a query to subscribe to a message queue for processing.
    /// </summary>
    /// <remarks>This query is used to initiate a subscription to a specific message queue. The subscription
    /// allows the caller to process messages from the specified queue.</remarks>
    /// <param name="queueName"></param>
    public class CMqSubscribeToQueueQuery(string queueName)
        : IRmQuery<CMqSubscribeToQueueQueryReply>
    {
        /// <summary>
        /// Gets or sets the name of the queue used for message processing.
        /// </summary>
        public string QueueName { get; set; } = queueName;
    }

    /// <summary>
    /// 
    /// </summary>
    public class CMqSubscribeToQueueQueryReply
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
        /// Initializes a new instance of the <see cref="CMqSubscribeToQueueQueryReply"/> class,  representing a failed
        /// subscription attempt to a message queue.
        /// </summary>
        /// <param name="exception">The exception that caused the subscription attempt to fail.  The exception's message is used to populate the
        /// <see cref="ErrorMessage"/> property.</param>
        public CMqSubscribeToQueueQueryReply(Exception exception)
        {
            IsSuccess = false;
            ErrorMessage = exception.Message;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CMqSubscribeToQueueQueryReply"/> class.
        /// </summary>
        /// <param name="isSuccess">A value indicating whether the subscription to the queue query was successful. <see langword="true"/> if the
        /// subscription succeeded; otherwise, <see langword="false"/>.</param>
        public CMqSubscribeToQueueQueryReply(bool isSuccess)
        {
            IsSuccess = isSuccess;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CMqSubscribeToQueueQueryReply"/> class.
        /// </summary>
        /// <remarks>This constructor creates an instance of the <see
        /// cref="CMqSubscribeToQueueQueryReply"/> class. Use this class to handle replies for queue subscription
        /// queries in a message queue system.</remarks>
        public CMqSubscribeToQueueQueryReply()
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
