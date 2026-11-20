namespace NTDLS.CatMQ.Shared
{
    /// <summary>
    /// A message received by the server.
    /// </summary>
    public class CMqReceivedMessage(string queueName, ulong serialNumber, string? messageType, string body)
    {
        /// <summary>
        /// Provides access to the custom serialization provider, if configured.
        /// </summary>
        public ICMqSerializationProvider? SerializationProvider { get; set; }

        /// <summary>
        /// The name of the queue which this message came from.
        /// </summary>
        public string QueueName { get; set; } = queueName;

        /// <summary>
        /// The name of the message type. Null when the publisher did not supply one.
        /// </summary>
        public string? MessageType { get; set; } = messageType;

        /// <summary>
        /// The message body.
        /// </summary>
        public string Body { get; set; } = body;

        /// <summary>
        /// The media type of the body.
        /// </summary>
        public string ContentType { get; set; } = CMqDefaults.DEFAULT_CONTENT_TYPE;

        /// <summary>
        /// Optional application-defined headers.
        /// </summary>
        public Dictionary<string, string>? Headers { get; set; }

        /// <summary>
        /// Whether the body is base64 encoded binary content.
        /// </summary>
        public bool BodyIsBase64 { get; set; }

        /// <summary>
        /// Gets the raw bytes of the body, decoding it if it is base64 encoded.
        /// </summary>
        public byte[] GetBodyBytes()
            => BodyIsBase64 ? Convert.FromBase64String(Body) : System.Text.Encoding.UTF8.GetBytes(Body);

        /// <summary>
        /// The unique ID of the message per queue.
        /// </summary>
        public ulong SerialNumber { get; set; } = serialNumber;

        /// <summary>
        /// The globally unique ID of the message. It does not change when the message is redelivered, so it can be used to detect a message that was already handled.
        /// </summary>
        public Guid MessageId { get; set; }

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
        /// How many times this message has been delivered to this consumer, including this time.
        /// </summary>
        public int DeliveryAttempt { get; set; }

        /// <summary>
        /// The number of subscribers that this message has been successfully delivered to.
        /// </summary>
        public int SubscriberDeliveryCount { get; set; }

        /// <summary>
        /// The number of subscribers that have responded to the delivery of this message.
        /// </summary>
        public int SatisfiedSubscriberCount { get; set; }

        /// <summary>
        /// The number of subscribers deliveries that have failed.
        /// </summary>
        public int FailedSubscriberCount { get; set; }

        /// <summary>
        /// Deserializes the received message to its original type.
        /// </summary>
        public ICMqMessage Deserialize()
            => CMqSerialization.Deserialize(this);

        /// <summary>
        /// Deserializes the received message to the given type. This also works for messages from publishers that do not
        /// use .NET types, because the type is only used when it resolves to a compatible .NET type.
        /// </summary>
        public T? Deserialize<T>() where T : class
            => CMqSerialization.Deserialize<T>(this);
    }
}
