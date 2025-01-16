namespace NTDLS.CatMQ.Server.Management
{
    /// <summary>
    /// Contains readonly information about messages.
    /// </summary>
    public class CMqEnqueuedMessageDescriptor
    {
        /// <summary>
        /// Creates a new instance of the CMqEnqueuedMessageDescriptor.
        /// </summary>
        /// <param name="serialNumber"></param>
        public CMqEnqueuedMessageDescriptor(string serialNumber)
        {
            SerialNumber = serialNumber;
        }

        /// <summary>
        /// The unique ID of the message.
        /// </summary>
        public string? SerialNumber { get; internal set; }

        /// <summary>
        /// The UTC date and time when the message was enqueued.
        /// </summary>
        public DateTime Timestamp { get; internal set; } = DateTime.UtcNow;

        /// <summary>
        /// The full assembly qualified name of the type of MessageJson.
        /// </summary>
        public string AssemblyQualifiedTypeName { get; internal set; } = string.Empty;

        /// <summary>
        /// The message payload that needs to be sent to the subscriber.
        /// </summary>
        public string MessageJson { get; internal set; } = string.Empty;

        /// <summary>
        /// The number of subscribers expecting to receive this message.
        /// </summary>
        public int SubscriberCount { get; internal set; }

        /// <summary>
        /// The UTC date and time, when if set, that the message will be delivered.
        /// </summary>
        public DateTime? DeferredUntil { get; set; }

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
