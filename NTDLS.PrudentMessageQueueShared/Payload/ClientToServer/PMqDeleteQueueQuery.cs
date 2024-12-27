using NTDLS.ReliableMessaging;

namespace NTDLS.PrudentMessageQueueShared.Payloads.Queries.ClientToServer
{
    public class PMqDeleteQueueQuery(string queueName)
        : IRmQuery<PMqDeleteQueueQueryReply>
    {
        public string QueueName { get; set; } = queueName;
    }

    public class PMqDeleteQueueQueryReply
        : IRmQueryReply
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }

        public PMqDeleteQueueQueryReply(Exception exception)
        {
            IsSuccess = false;
            ErrorMessage = exception.Message;
        }

        public PMqDeleteQueueQueryReply(bool isSuccess)
        {
            IsSuccess = isSuccess;
        }

        public PMqDeleteQueueQueryReply()
        {
        }
    }
}
