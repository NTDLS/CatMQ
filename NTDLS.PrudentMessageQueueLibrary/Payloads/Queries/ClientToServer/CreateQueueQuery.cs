using NTDLS.ReliableMessaging;

namespace NTDLS.PrudentMessageQueueLibrary.Payloads.Queries.ClientToServer
{
    public class CreateQueueQuery(PMqQueueConfiguration queueConfiguration)
        : IRmQuery<CreateQueueQueryReply>
    {
        public PMqQueueConfiguration QueueConfiguration { get; set; } = queueConfiguration;
    }

    public class CreateQueueQueryReply
        : IRmQueryReply
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }

        public CreateQueueQueryReply(Exception exception)
        {
            IsSuccess = false;
            ErrorMessage = exception.Message;
        }

        public CreateQueueQueryReply(bool isSuccess)
        {
            IsSuccess = isSuccess;
        }

        public CreateQueueQueryReply()
        {
        }
    }
}
