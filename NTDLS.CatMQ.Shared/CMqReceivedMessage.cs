namespace NTDLS.CatMQ.Shared
{
    /// <summary>
    /// A message received by the server.
    /// </summary>
    public class CMqReceivedMessage(string queueName, string objectType, string messageJson)
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
        public string ObjectType { get; set; } = objectType;

        /// <summary>
        /// Json serialized object of type denoted by ObjectType.
        /// </summary>
        public string MessageJson { get; set; } = messageJson;

        /// <summary>
        /// Deserializes the received message to its original type.
        /// </summary>
        public ICMqMessage Deserialize()
            => CMqUnboxing.Unbox(this);

        /// <summary>
        /// Deserializes the received message to its original type.
        /// </summary>
        public T? Deserialize<T>() where T : class
            => CMqUnboxing.Unbox(this) as T;
    }
}
