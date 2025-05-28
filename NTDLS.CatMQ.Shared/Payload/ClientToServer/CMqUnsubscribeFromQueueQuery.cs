using NTDLS.ReliableMessaging;

namespace NTDLS.CatMQ.Shared.Payload.ClientToServer
{
    /// <summary>
    /// Represents a query to unsubscribe from a message queue.
    /// </summary>
    /// <remarks>This query is used to request the removal of a subscription to a specific message queue. The
    /// <see cref="QueueName"/> property specifies the name of the queue to unsubscribe from.</remarks>
    /// <param name="queueName">The name of the message queue to unsubscribe from. Cannot be null or empty.</param>
    public class CMqUnsubscribeFromQueueQuery(string queueName)
        : IRmQuery<CMqUnsubscribeFromQueueQueryReply>
    {
        /// <summary>
        /// Gets or sets the name of the queue.
        /// </summary>
        public string QueueName { get; set; } = queueName;
    }

    /// <summary>
    /// Represents the reply to an unsubscribe operation from a message queue.
    /// </summary>
    /// <remarks>This class encapsulates the result of an attempt to unsubscribe from a message queue, 
    /// including whether the operation was successful and any associated error message.</remarks>
    public class CMqUnsubscribeFromQueueQueryReply
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
        /// Initializes a new instance of the <see cref="CMqUnsubscribeFromQueueQueryReply"/> class,  representing a
        /// failed attempt to unsubscribe from a queue.
        /// </summary>
        /// <remarks>This constructor sets the <see cref="IsSuccess"/> property to <see langword="false"/>
        /// and  populates the <see cref="ErrorMessage"/> property with the message from the provided
        /// exception.</remarks>
        /// <param name="exception">The exception that occurred during the unsubscribe operation.</param>
        public CMqUnsubscribeFromQueueQueryReply(Exception exception)
        {
            IsSuccess = false;
            ErrorMessage = exception.Message;
        }

        /// <summary>
        /// Represents the reply to a query for unsubscribing from a message queue.
        /// </summary>
        /// <param name="isSuccess">A value indicating whether the unsubscription operation was successful.</param>
        public CMqUnsubscribeFromQueueQueryReply(bool isSuccess)
        {
            IsSuccess = isSuccess;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CMqUnsubscribeFromQueueQueryReply"/> class.
        /// </summary>
        /// <remarks>This constructor creates an instance of the <see
        /// cref="CMqUnsubscribeFromQueueQueryReply"/> class. Use this class to handle replies for unsubscribe queries
        /// in a message queue system.</remarks>
        public CMqUnsubscribeFromQueueQueryReply()
        {
        }
    }
}
