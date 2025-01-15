using System.Text.Json.Serialization;

namespace NTDLS.CatMQ.Server.Server
{
    /// <summary>
    /// A message that is in the queue and waiting to be delivered to all subscribers.
    /// </summary>
    internal class EnqueuedMessage(string queueName, string assemblyQualifiedTypeName, string messageJson)
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
        /// The UTC date and time, when if set, that the message will be delivered.
        /// </summary>
        public DateTime? DeferredUntil { get; set; }

        /// <summary>
        /// The full assembly qualified name of the type of MessageJson.
        /// </summary>
        public string AssemblyQualifiedTypeName { get; set; } = assemblyQualifiedTypeName;

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
        public HashSet<Guid> SatisfiedSubscribersSubscriberIDs { get; set; } = new();

        /// <summary>
        /// List of subscribers which failed to be delivered to or for which the retry-attempts have been reached.
        /// </summary>
        [JsonIgnore] //Serialize using System.Text.Json as opposed to Newtonsoft for efficiency.
        public HashSet<Guid> FailedSubscribersSubscriberIDs { get; set; } = new();
    }
}
