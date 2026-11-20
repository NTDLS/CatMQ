using NTDLS.CatMQ.Shared;
using System.Text.Json.Serialization;

namespace NTDLS.CatMQ.Server.Server
{
    /// <summary>
    /// A message that is in the queue and waiting to be delivered to all subscribers.
    /// </summary>
    internal class EnqueuedMessage(string queueName, string? messageType, string body, ulong serialNumber)
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
        /// The globally unique ID of the message. It is assigned when the message is enqueued, survives restarts and is kept when the message is dead-lettered,
        /// so that subscribers can use it to detect a message that they have already handled. Guid.Empty only for messages persisted by older versions,
        /// which are given an ID when they are loaded.
        /// </summary>
        public Guid MessageId { get; set; } = Guid.Empty;

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
        /// The name of the message type, which is optional and meaningful only to publishers and consumers.
        /// </summary>
        public string? MessageType { get; set; } = messageType;

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
        /// Reads MessageType from messages persisted by older versions. It is never written.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? AssemblyQualifiedTypeName
        {
            get => null;
            set => MessageType ??= value;
        }

        /// <summary>
        /// The message payload that needs to be sent to the subscriber.
        /// </summary>
        public string Body { get; set; } = body;

        /// <summary>
        /// Reads Body from messages persisted by older versions. It is never written.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? MessageJson
        {
            get => null;
            set
            {
                if (value != null)
                {
                    Body = value;
                }
            }
        }

        /// <summary>
        /// Dictionary of subscriber IDs and any per-subscriber statistics.
        /// </summary>
        [JsonIgnore]
        public Dictionary<Guid, SubscriberMessageDeliveryStatistics> SubscriberMessageDeliveries { get; set; } = new();

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
        public HashSet<Guid> DeliveryLimitReachedSubscriberIDs { get; set; } = new();

        public EnqueuedMessage CloneForDeadLetter(string queueName, ulong serialNumber)
        {
            return new EnqueuedMessage(queueName, MessageType, Body, serialNumber)
            {
                ContentType = ContentType,
                Headers = Headers,
                BodyIsBase64 = BodyIsBase64,
                MessageId = MessageId,
                Timestamp = Timestamp,
                DeferredCount = DeferredCount
            };
        }
    }
}
