using NTDLS.CatMQ.Shared.Payload.ServerToClient;
using NTDLS.ReliableMessaging;

namespace NTDLS.CatMQ.Client.Client.QueryHandlers
{
    internal class InternalClientQueryHandlers(CMqClient mqClient)
        : IRmMessageHandler
    {
        /// <summary>
        /// The client has received a message from the server which needs to be consumed.
        /// </summary>
        public CMqMessageDeliveryQueryReply MessageDeliveryQuery(RmContext context, CMqMessageDeliveryQuery param)
        {
            try
            {
                var message = new CMqReceivedMessage(param.QueueName, param.ObjectType, param.MessageJson);
                var wasConsumed = mqClient.InvokeOnReceived(mqClient, message);
                return new CMqMessageDeliveryQueryReply(wasConsumed);
            }
            catch (Exception ex)
            {
                mqClient.InvokeOnException(mqClient, null, ex);
                return new CMqMessageDeliveryQueryReply(ex.GetBaseException());
            }
        }
    }
}
