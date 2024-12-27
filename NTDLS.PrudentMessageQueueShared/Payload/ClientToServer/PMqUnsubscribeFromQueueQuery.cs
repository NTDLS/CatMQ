using NTDLS.ReliableMessaging;

namespace NTDLS.PrudentMessageQueueShared.Payloads.Queries.ClientToServer
{
    public class PMqUnsubscribeFromQueueQuery(string queueName)
        : IRmQuery<PMqUnsubscribeFromQueueQueryReply>
    {
        public string QueueName { get; set; } = queueName;
    }

    public class PMqUnsubscribeFromQueueQueryReply
        : IRmQueryReply
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }

        public PMqUnsubscribeFromQueueQueryReply(Exception exception)
        {
            IsSuccess = false;
            ErrorMessage = exception.Message;
        }

        public PMqUnsubscribeFromQueueQueryReply(bool isSuccess)
        {
            IsSuccess = isSuccess;
        }

        public PMqUnsubscribeFromQueueQueryReply()
        {
        }
    }
}
