using NTDLS.ReliableMessaging;

namespace NTDLS.CatMQ.Shared.Payload.ClientToServer
{
    public class CMqEnqueueMessageToQueue(string queueName, TimeSpan? deferDeliveryDuration, string objectType, string messageJson)
        : IRmQuery<CMqEnqueueMessageToQueueReply>
    {
        public string QueueName { get; set; } = queueName;

        /// <summary>
        /// The amount of time, when if set, in which the server will delay delivery of the message.
        /// </summary>
        public TimeSpan? DeferDeliveryDuration { get; set; } = deferDeliveryDuration;

        /// <summary>
        /// The full assembly qualified name of the type of MessageJson.
        /// </summary>
        public string ObjectType { get; set; } = objectType;
        public string MessageJson { get; set; } = messageJson;
    }

    public class CMqEnqueueMessageToQueueReply
        : IRmQueryReply
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }

        public CMqEnqueueMessageToQueueReply(Exception exception)
        {
            IsSuccess = false;
            ErrorMessage = exception.Message;
        }

        public CMqEnqueueMessageToQueueReply(bool isSuccess)
        {
            IsSuccess = isSuccess;
        }

        public CMqEnqueueMessageToQueueReply()
        {
        }
    }
}
