using NTDLS.ReliableMessaging;

namespace NTDLS.CatMQ.Shared.Payloads.Queries.ClientToServer
{
    public class CMqDeleteQueueQuery(string queueName)
        : IRmQuery<CMqDeleteQueueQueryReply>
    {
        public string QueueName { get; set; } = queueName;
    }

    public class CMqDeleteQueueQueryReply
        : IRmQueryReply
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }

        public CMqDeleteQueueQueryReply(Exception exception)
        {
            IsSuccess = false;
            ErrorMessage = exception.Message;
        }

        public CMqDeleteQueueQueryReply(bool isSuccess)
        {
            IsSuccess = isSuccess;
        }

        public CMqDeleteQueueQueryReply()
        {
        }
    }
}
