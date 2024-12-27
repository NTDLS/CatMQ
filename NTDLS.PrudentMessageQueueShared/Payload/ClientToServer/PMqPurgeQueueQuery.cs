using NTDLS.ReliableMessaging;

namespace NTDLS.PrudentMessageQueueShared.Payloads.Queries.ClientToServer
{
    public class PMqPurgeQueueQuery(string queueName)
        : IRmQuery<PMqPurgeQueueQueryReply>
    {
        public string QueueName { get; set; } = queueName;
    }

    public class PMqPurgeQueueQueryReply
        : IRmQueryReply
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }

        public PMqPurgeQueueQueryReply(Exception exception)
        {
            IsSuccess = false;
            ErrorMessage = exception.Message;
        }

        public PMqPurgeQueueQueryReply(bool isSuccess)
        {
            IsSuccess = isSuccess;
        }

        public PMqPurgeQueueQueryReply()
        {
        }
    }
}
