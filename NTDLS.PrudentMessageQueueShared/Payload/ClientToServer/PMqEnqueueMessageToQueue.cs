using NTDLS.ReliableMessaging;

namespace NTDLS.PrudentMessageQueueShared.Payloads.Queries.ClientToServer
{
    public class PMqEnqueueMessageToQueue(string queueName, string objectType, string messageJson)
        : IRmQuery<PMqEnqueueMessageToQueueReply>
    {
        public string QueueName { get; set; } = queueName;
        /// <summary>
        /// The full assembly qualified name of the type of MessageJson.
        /// </summary>
        public string ObjectType { get; set; } = objectType;
        public string MessageJson { get; set; } = messageJson;
    }

    public class PMqEnqueueMessageToQueueReply
        : IRmQueryReply
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }

        public PMqEnqueueMessageToQueueReply(Exception exception)
        {
            IsSuccess = false;
            ErrorMessage = exception.Message;
        }

        public PMqEnqueueMessageToQueueReply(bool isSuccess)
        {
            IsSuccess = isSuccess;
        }

        public PMqEnqueueMessageToQueueReply()
        {
        }
    }
}
