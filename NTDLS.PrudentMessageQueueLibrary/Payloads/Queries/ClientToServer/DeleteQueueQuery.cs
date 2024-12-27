using NTDLS.ReliableMessaging;

namespace NTDLS.PrudentMessageQueueLibrary.Payloads.Queries.ClientToServer
{
    public class DeleteQueueQuery(string queueName)
        : IRmQuery<DeleteQueueQueryReply>
    {
        public string QueueName { get; set; } = queueName;
    }

    public class DeleteQueueQueryReply
        : IRmQueryReply
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }

        public DeleteQueueQueryReply(Exception exception)
        {
            IsSuccess = false;
            ErrorMessage = exception.Message;
        }

        public DeleteQueueQueryReply(bool isSuccess)
        {
            IsSuccess = isSuccess;
        }

        public DeleteQueueQueryReply()
        {
        }
    }
}
