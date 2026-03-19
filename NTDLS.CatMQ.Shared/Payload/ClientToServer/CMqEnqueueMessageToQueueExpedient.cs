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
    public class CMqEnqueueMessageToQueueExpedient(string queueName, TimeSpan? deferDeliveryDuration, string objectType, string messageJson)
        : IRmNotification
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
}
