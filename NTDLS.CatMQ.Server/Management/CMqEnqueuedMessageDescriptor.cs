using NTDLS.CatMQ.Shared;

namespace NTDLS.CatMQ.Server.Management
{
    /// <summary>
    /// Contains readonly information about messages and various statistics.
    /// </summary>
    public class CMqEnqueuedMessageDescriptor(ulong serialNumber)
    {
        /// <summary>
        /// The unique ID of the message per queue.
        /// </summary>
        public ulong SerialNumber { get; internal set; } = serialNumber;

        /// <summary>
        /// The globally unique ID of the message.
        /// </summary>
        public Guid MessageId { get; internal set; }

        /// <summary>
        /// The UTC date and time when the message was enqueued.
        /// </summary>
        public DateTime Timestamp { get; internal set; } = DateTime.UtcNow;

        /// <summary>
        /// The name of the message type. Null when the publisher did not supply one.
        /// </summary>
        public string? MessageType { get; internal set; }

        /// <summary>
        /// The media type of the body.
        /// </summary>
        public string ContentType { get; internal set; } = CMqDefaults.DEFAULT_CONTENT_TYPE;

        /// <summary>
        /// Optional application-defined headers.
        /// </summary>
        public Dictionary<string, string>? Headers { get; internal set; }

        /// <summary>
        /// Whether the body is base64 encoded binary content.
        /// </summary>
        public bool BodyIsBase64 { get; internal set; }

        /// <summary>
        /// The message payload that needs to be sent to the subscriber.
        /// </summary>
        public string Body { get; internal set; } = string.Empty;

        /// <summary>
        /// The number of subscribers expecting to receive this message.
        /// </summary>
        public int SubscriberCount { get; internal set; }

        /// <summary>
        /// The UTC date and time, when if set, that the message will be delivered.
        /// </summary>
        public DateTime? DeferredUntil { get; set; }

        /// <summary>
        /// Gets or sets the number of times delivery of this message has been deferred.
        /// </summary>
        public int DeferredDeliveryCount { get; set; }

        /// <summary>
        /// The list of connection IDs that the message has been successfully delivered to.
        /// </summary>
        public HashSet<Guid> SubscriberMessageDeliveries { get; internal set; } = new();

        /// <summary>
        /// List of subscribers which have been delivered to or for which the retry-attempts have been reached.
        /// </summary>
        public HashSet<Guid> SatisfiedSubscribersSubscriberIDs { get; internal set; } = new();
    }
}
