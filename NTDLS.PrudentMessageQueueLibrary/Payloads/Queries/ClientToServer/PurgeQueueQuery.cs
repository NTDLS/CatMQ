using NTDLS.ReliableMessaging;

namespace NTDLS.PrudentMessageQueueLibrary.Payloads.Queries.ClientToServer
{
    public class PurgeQueueQuery(string queueName)
        : IRmQuery<PurgeQueueQueryReply>
    {
        public string QueueName { get; set; } = queueName;
    }

    public class PurgeQueueQueryReply
        : IRmQueryReply
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }

        public PurgeQueueQueryReply(Exception exception)
        {
            IsSuccess = false;
            ErrorMessage = exception.Message;
        }

        public PurgeQueueQueryReply(bool isSuccess)
        {
            IsSuccess = isSuccess;
        }

        public PurgeQueueQueryReply()
        {
        }
    }
}
