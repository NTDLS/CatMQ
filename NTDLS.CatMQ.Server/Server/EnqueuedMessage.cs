using System.Text.Json.Serialization;

namespace NTDLS.CatMQ.Server.Server
{
    /// <summary>
    /// A message that is in the queue and waiting to be delivered to all subscribers.
    /// </summary>
    internal class EnqueuedMessage(string queueName, string objectType, string messageJson)
    {
        /// <summary>
        /// The name of the queue which contains this message.
        /// </summary>
        public string QueueName { get; set; } = queueName;

        /// <summary>
        /// The unique ID of the message.
        /// </summary>
        public Guid MessageId { get; set; } = Guid.NewGuid();

        /// <summary>
        /// The UTC date and time when the message was enqueued.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The full assembly qualified name of the type of MessageJson.
        /// </summary>
        public string ObjectType { get; set; } = objectType;

        /// <summary>
        /// The message payload that needs to be sent to the subscriber.
        /// </summary>
        public string MessageJson { get; set; } = messageJson;

        /// <summary>
        /// The list of connection IDs that the message has been successfully delivered to.
        /// </summary>
        [JsonIgnore] //Serialize using System.Text.Json as opposed to Newtonsoft for efficiency.
        public Dictionary<Guid, SubscriberMessageDelivery> SubscriberMessageDeliveries { get; set; } = new();

        /// <summary>
        /// List of subscribers which have been delivered to or for which the retry-attempts have been reached.
        /// </summary>
        [JsonIgnore] //Serialize using System.Text.Json as opposed to Newtonsoft for efficiency.
        public HashSet<Guid> SatisfiedSubscribersConnectionIDs { get; set; } = new();
    }
}
