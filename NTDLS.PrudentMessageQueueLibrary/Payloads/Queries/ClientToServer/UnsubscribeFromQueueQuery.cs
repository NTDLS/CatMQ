using NTDLS.ReliableMessaging;

namespace NTDLS.PrudentMessageQueueLibrary.Payloads.Queries.ClientToServer
{
    public class UnsubscribeFromQueueQuery(string queueName)
        : IRmQuery<UnsubscribeFromQueueQueryReply>
    {
        public string QueueName { get; set; } = queueName;
    }

    public class UnsubscribeFromQueueQueryReply
        : IRmQueryReply
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }

        public UnsubscribeFromQueueQueryReply(Exception exception)
        {
            IsSuccess = false;
            ErrorMessage = exception.Message;
        }

        public UnsubscribeFromQueueQueryReply(bool isSuccess)
        {
            IsSuccess = isSuccess;
        }

        public UnsubscribeFromQueueQueryReply()
        {
        }
    }
}
