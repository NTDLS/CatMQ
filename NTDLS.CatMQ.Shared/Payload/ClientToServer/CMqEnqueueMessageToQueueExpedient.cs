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
    /// <param name="messageType"></param>
    /// <param name="body"></param>
    /// <param name="contentType"></param>
    /// <param name="headers"></param>
    /// <param name="bodyIsBase64"></param>
    public class CMqEnqueueMessageToQueueExpedient(string queueName, TimeSpan? deferDeliveryDuration, string? messageType, string body, string? contentType = null, Dictionary<string, string>? headers = null, bool bodyIsBase64 = false)
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
        /// The name of the message type, which is optional and meaningful only to publishers and consumers.
        /// </summary>
        public string? MessageType { get; set; } = messageType;

        /// <summary>
        /// The message body.
        /// </summary>
        public string Body { get; set; } = body;

        /// <summary>
        /// The media type of the body. Defaults to JSON when null.
        /// </summary>
        public string? ContentType { get; set; } = contentType;

        /// <summary>
        /// Optional application-defined headers.
        /// </summary>
        public Dictionary<string, string>? Headers { get; set; } = headers;

        /// <summary>
        /// Whether the body is base64 encoded binary content.
        /// </summary>
        public bool BodyIsBase64 { get; set; } = bodyIsBase64;
    }
}
