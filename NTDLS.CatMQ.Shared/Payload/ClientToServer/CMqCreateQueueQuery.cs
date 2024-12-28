using NTDLS.ReliableMessaging;

namespace NTDLS.CatMQ.Shared.Payload.ClientToServer
{
    public class CMqCreateQueueQuery(CMqQueueConfiguration queueConfiguration)
        : IRmQuery<CMqCreateQueueQueryReply>
    {
        public CMqQueueConfiguration QueueConfiguration { get; set; } = queueConfiguration;
    }

    public class CMqCreateQueueQueryReply
        : IRmQueryReply
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }

        public CMqCreateQueueQueryReply(Exception exception)
        {
            IsSuccess = false;
            ErrorMessage = exception.Message;
        }

        public CMqCreateQueueQueryReply(bool isSuccess)
        {
            IsSuccess = isSuccess;
        }

        public CMqCreateQueueQueryReply()
        {
        }
    }
}
