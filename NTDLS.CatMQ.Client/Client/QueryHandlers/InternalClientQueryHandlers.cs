using NTDLS.CatMQ.Shared;
using NTDLS.CatMQ.Shared.Payload.ServerToClient;
using NTDLS.ReliableMessaging;

namespace NTDLS.CatMQ.Client.Client.QueryHandlers
{
    internal class InternalClientQueryHandlers(CMqClient mqClient)
        : IRmMessageHandler
    {
        /// <summary>
        /// The client has received a message from the server which needs to be consumed.
        /// This is asynchronous so that asynchronous subscription handlers do not hold a thread while they are pending.
        /// </summary>
        public async Task<CMqMessageDeliveryQueryReply> MessageDeliveryQuery(RmContext context, CMqMessageDeliveryQuery param)
        {
            try
            {
                var message = new CMqReceivedMessage(param.QueueName, param.SerialNumber, param.MessageType, param.Body)
                {
                    MessageId = param.MessageId,
                    ContentType = param.ContentType,
                    Headers = param.Headers,
                    BodyIsBase64 = param.BodyIsBase64,
                    Timestamp = param.Timestamp,
                    DeferredCount = param.DeferredCount,
                    DeferDuration = param.DeferDuration,
                    SubscriberDeliveryCount = param.SubscriberDeliveryCount,
                    SatisfiedSubscriberCount = param.SatisfiedSubscriberCount,
                    FailedSubscriberCount = param.FailedSubscriberCount
                };
                var messageDisposition = await mqClient.InvokeOnReceivedAsync(mqClient, message);
                return new CMqMessageDeliveryQueryReply(messageDisposition);
            }
            catch (Exception ex)
            {
                mqClient.InvokeOnException(mqClient, null, ex);
                return new CMqMessageDeliveryQueryReply(ex.GetBaseException());
            }
        }
    }
}
