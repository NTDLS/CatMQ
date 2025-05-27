using NTDLS.ReliableMessaging;

namespace NTDLS.CatMQ.Shared.Payload.ClientToServer
{
    /// <summary>
    /// Represents a request to enqueue a message into a specified queue, with optional deferred delivery and type
    /// metadata.
    /// </summary>
    /// <remarks>This class is used to encapsulate the details of a message being enqueued into a queue,
    /// including the queue name,  optional delivery delay, the type of the message object, and the serialized message
    /// content.</remarks>
    /// <param name="queueName"></param>
    /// <param name="deferDeliveryDuration"></param>
    /// <param name="objectType"></param>
    /// <param name="messageJson"></param>
    public class CMqEnqueueMessageToQueue(string queueName, TimeSpan? deferDeliveryDuration, string objectType, string messageJson)
        : IRmQuery<CMqEnqueueMessageToQueueReply>
    {
        /// <summary>
        /// Gets or sets the name of the queue.
        /// </summary>
        public string QueueName { get; set; } = queueName;

        /// <summary>
        /// The amount of time, when if set, in which the server will delay delivery of the message.
        /// </summary>
        public TimeSpan? DeferDeliveryDuration { get; set; } = deferDeliveryDuration;

        /// <summary>
        /// The full assembly qualified name of the type of MessageJson.
        /// </summary>
        public string ObjectType { get; set; } = objectType;

        /// <summary>
        /// Gets or sets the JSON representation of the message.
        /// </summary>
        public string MessageJson { get; set; } = messageJson;
    }

    /// <summary>
    /// Represents the reply to an enqueue message operation in a message queue.
    /// </summary>
    /// <remarks>This class provides information about the result of an enqueue operation, including whether
    /// the operation  was successful and any associated error message. It can be used to inspect the outcome of the
    /// operation  and handle errors accordingly.</remarks>
    public class CMqEnqueueMessageToQueueReply
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
        /// Initializes a new instance of the <see cref="CMqEnqueueMessageToQueueReply"/> class,  representing a failed
        /// enqueue operation with the specified exception details.
        /// </summary>
        /// <param name="exception">The exception that caused the enqueue operation to fail.  The <see cref="ErrorMessage"/> property will be
        /// populated with the exception's message.</param>
        public CMqEnqueueMessageToQueueReply(Exception exception)
        {
            IsSuccess = false;
            ErrorMessage = exception.Message;
        }

        /// <summary>
        /// Represents the reply received after attempting to enqueue a message to a queue.
        /// </summary>
        /// <param name="isSuccess">A value indicating whether the enqueue operation was successful.  <see langword="true"/> if the operation
        /// succeeded; otherwise, <see langword="false"/>.</param>
        public CMqEnqueueMessageToQueueReply(bool isSuccess)
        {
            IsSuccess = isSuccess;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CMqEnqueueMessageToQueueReply"/> class.
        /// </summary>
        /// <remarks>This constructor creates an instance of the <see
        /// cref="CMqEnqueueMessageToQueueReply"/> class. Use this class to represent the reply or response after a
        /// message is enqueued to a queue.</remarks>
        public CMqEnqueueMessageToQueueReply()
        {
        }
    }
}
