namespace NTDLS.CatMQ.Shared
{
    /// <summary>
    /// A message received by the server.
    /// </summary>
    public class CMqReceivedMessage(string queueName, ulong serialNumber, string assemblyQualifiedTypeName, string messageJson)
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
        /// The assembly qualified type name of the object stored in MessageJson.
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

        /// <summary>
        /// Deserializes the received message to its original type.
        /// </summary>
        public ICMqMessage Deserialize()
            => CMqSerialization.Deserialize(this);

        /// <summary>
        /// Deserializes the received message to its original type, casted to the given type.
        /// </summary>
        public T? Deserialize<T>() where T : class
            => CMqSerialization.Deserialize(this) as T;
    }
}
