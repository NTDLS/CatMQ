using NTDLS.ReliableMessaging;

namespace NTDLS.CatMQ.Shared.Payload.ClientToServer
{
    public class CMqUnsubscribeFromQueueQuery(string queueName)
        : IRmQuery<CMqUnsubscribeFromQueueQueryReply>
    {
        public string QueueName { get; set; } = queueName;
    }

    public class CMqUnsubscribeFromQueueQueryReply
        : IRmQueryReply
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }

        public CMqUnsubscribeFromQueueQueryReply(Exception exception)
        {
            IsSuccess = false;
            ErrorMessage = exception.Message;
        }

        public CMqUnsubscribeFromQueueQueryReply(bool isSuccess)
        {
            IsSuccess = isSuccess;
        }

        public CMqUnsubscribeFromQueueQueryReply()
        {
        }
    }
}
