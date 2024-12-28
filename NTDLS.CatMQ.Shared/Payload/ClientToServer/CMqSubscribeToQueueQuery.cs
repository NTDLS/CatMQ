using NTDLS.ReliableMessaging;

namespace NTDLS.CatMQ.Shared.Payloads.Queries.ClientToServer
{
    public class CMqSubscribeToQueueQuery(string queueName)
        : IRmQuery<CMqSubscribeToQueueQueryReply>
    {
        public string QueueName { get; set; } = queueName;
    }

    public class CMqSubscribeToQueueQueryReply
        : IRmQueryReply
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }

        public CMqSubscribeToQueueQueryReply(Exception exception)
        {
            IsSuccess = false;
            ErrorMessage = exception.Message;
        }

        public CMqSubscribeToQueueQueryReply(bool isSuccess)
        {
            IsSuccess = isSuccess;
        }

        public CMqSubscribeToQueueQueryReply()
        {
        }
    }
}
