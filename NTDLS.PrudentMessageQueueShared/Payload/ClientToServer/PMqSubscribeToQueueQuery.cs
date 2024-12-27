using NTDLS.ReliableMessaging;

namespace NTDLS.PrudentMessageQueueShared.Payloads.Queries.ClientToServer
{
    public class PMqSubscribeToQueueQuery(string queueName)
        : IRmQuery<PMqSubscribeToQueueQueryReply>
    {
        public string QueueName { get; set; } = queueName;
    }

    public class PMqSubscribeToQueueQueryReply
        : IRmQueryReply
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }

        public PMqSubscribeToQueueQueryReply(Exception exception)
        {
            IsSuccess = false;
            ErrorMessage = exception.Message;
        }

        public PMqSubscribeToQueueQueryReply(bool isSuccess)
        {
            IsSuccess = isSuccess;
        }

        public PMqSubscribeToQueueQueryReply()
        {
        }
    }
}
