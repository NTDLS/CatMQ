using NTDLS.CatMQ.Shared;
using System.Text.Json.Serialization;

namespace NTDLS.CatMQ.Server.Server
{
    /// <summary>
    /// A message that is in the queue and waiting to be delivered to all subscribers.
    /// </summary>
    internal class EnqueuedMessage(string queueName, string assemblyQualifiedTypeName, string messageJson, ulong serialNumber)
    {
        [JsonIgnore]
        public CMqMessageState State { get; set; } = CMqMessageState.Ready;

        /// <summary>
        /// The name of the queue which contains this message.
        /// </summary>
        public string QueueName { get; set; } = queueName;

        /// <summary>
        /// The unique ID of the message per queue.
        /// </summary>
        public ulong SerialNumber { get; set; } = serialNumber;

        /// <summary>
        /// The UTC date and time when the message was enqueued.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The number of times this message has been delivered and the subscriber requested that delivery be deferred.
        /// </summary>
        public int DeferredCount { get; set; }

        /// <summary>
        /// The UTC date and time, when if set, that the message will be delivered.
        /// </summary>
        public DateTime? DeferredUntil { get; set; }

        /// <summary>
        /// The amount of time to wait before delivering this message.
        /// </summary>
        public TimeSpan? DeferDuration { get; set; }

        /// <summary>
        /// The full assembly qualified name of the type of MessageJson.
        /// </summary>
        public string AssemblyQualifiedTypeName { get; set; } = assemblyQualifiedTypeName;

        /// <summary>
        /// The message payload that needs to be sent to the subscriber.
        /// </summary>
        public string MessageJson { get; set; } = messageJson;

        /// <summary>
        /// Dictionary of subscriber IDs and any per-subscriber statistics.
        /// </summary>
        [JsonIgnore]
        public Dictionary<Guid, SubscriberMessageDelivery> SubscriberMessageDeliveries { get; set; } = new();

        /// <summary>
        /// Subscribers for which the delivery was successful and received a "consumed" or "non-consumed" response.
        /// </summary>
        [JsonIgnore]
        public HashSet<Guid> SatisfiedDeliverySubscriberIDs { get; set; } = new();

        /// <summary>
        /// Subscribers which have been successfully delivered to and a response with a consumption result.
        /// </summary>
        [JsonIgnore]
        public HashSet<Guid> ConsumedDeliverySubscriberIDs { get; set; } = new();

        /// <summary>
        /// Subscribers where the failed delivery count has reached the retry-attempt limit.
        /// </summary>
        [JsonIgnore]
        public HashSet<Guid> FailedDeliverySubscriberIDs { get; set; } = new();

        public EnqueuedMessage CloneForDeadLetter(string queueName, ulong serialNumber)
        {
            return new EnqueuedMessage(queueName, AssemblyQualifiedTypeName, MessageJson, serialNumber)
            {
                Timestamp = Timestamp,
                DeferredCount = DeferredCount
            };
        }
    }
}
