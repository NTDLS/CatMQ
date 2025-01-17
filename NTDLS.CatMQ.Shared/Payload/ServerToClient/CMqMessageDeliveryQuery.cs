using NTDLS.ReliableMessaging;

namespace NTDLS.CatMQ.Shared.Payload.ServerToClient
{
    public class CMqMessageDeliveryQuery(string queueName, string serialNumber, string assemblyQualifiedTypeName, string messageJson)
    : IRmQuery<CMqMessageDeliveryQueryReply>
    {
        /// <summary>
        /// The name of the queue from which this message is being delivered.
        /// </summary>
        public string QueueName { get; set; } = queueName;

        /// <summary>
        /// The full assembly qualified name of the type of MessageJson.
        /// </summary>
        public string AssemblyQualifiedTypeName { get; set; } = assemblyQualifiedTypeName;

        /// <summary>
        /// Json serialized object of type denoted by ObjectType.
        /// </summary>
        public string MessageJson { get; set; } = messageJson;

        /// <summary>
        /// The unique ID of the message.
        /// </summary>
        public string SerialNumber { get; set; } = serialNumber;

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
    }

    public class CMqMessageDeliveryQueryReply
        : IRmQueryReply
    {
        /// <summary>
        /// Denotes whether the client which received the delivery notification consumed
        /// the message. The message will not leave the top of the queue until it has been
        /// consumed by all subscribers (or the message expires or is dead-lettered).
        /// </summary>
        public CMqConsumeResult ConsumeResult { get; set; }
        public string? ErrorMessage { get; set; }

        public CMqMessageDeliveryQueryReply(Exception exception)
        {
            ConsumeResult = new CMqConsumeResult(CMqConsumptionDisposition.NotConsumed);
            ErrorMessage = exception.Message;
        }

        public CMqMessageDeliveryQueryReply(CMqConsumeResult consumeResult)
        {
            ConsumeResult = consumeResult;
        }

        public CMqMessageDeliveryQueryReply()
        {
            ConsumeResult = new CMqConsumeResult(CMqConsumptionDisposition.NotConsumed);
        }
    }
}
