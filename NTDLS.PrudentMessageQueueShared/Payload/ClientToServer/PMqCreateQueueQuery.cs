using NTDLS.ReliableMessaging;

namespace NTDLS.PrudentMessageQueueShared.Payloads.Queries.ClientToServer
{
    public class PMqCreateQueueQuery(PMqQueueConfiguration queueConfiguration)
        : IRmQuery<PMqCreateQueueQueryReply>
    {
        public PMqQueueConfiguration QueueConfiguration { get; set; } = queueConfiguration;
    }

    public class PMqCreateQueueQueryReply
        : IRmQueryReply
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }

        public PMqCreateQueueQueryReply(Exception exception)
        {
            IsSuccess = false;
            ErrorMessage = exception.Message;
        }

        public PMqCreateQueueQueryReply(bool isSuccess)
        {
            IsSuccess = isSuccess;
        }

        public PMqCreateQueueQueryReply()
        {
        }
    }
}
