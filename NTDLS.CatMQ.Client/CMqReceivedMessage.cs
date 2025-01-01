namespace NTDLS.CatMQ.Client
{
    /// <summary>
    /// A message received by the server.
    /// </summary>
    public class CMqReceivedMessage(string queueName, string objectType, string messageJson)
    {
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
        /// <returns></returns>
        public ICMqMessage Deserialize()
            => CMqUnboxing.Unbox(this);
    }
}
