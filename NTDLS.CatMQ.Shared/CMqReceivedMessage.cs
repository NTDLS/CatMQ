namespace NTDLS.CatMQ.Shared
{
    /// <summary>
    /// A message received by the server.
    /// </summary>
    public class CMqReceivedMessage(string queueName, string objectType, string messageJson)
    {
        public string QueueName { get; set; } = queueName;
        public string ObjectType { get; set; } = objectType;
        public string MessageJson { get; set; } = messageJson;

        /// <summary>
        /// Deserializes the received message to its original type.
        /// </summary>
        /// <returns></returns>
        public ICMqMessage Deserialize()
            => CMqUnboxing.Unbox(this);
    }
}
