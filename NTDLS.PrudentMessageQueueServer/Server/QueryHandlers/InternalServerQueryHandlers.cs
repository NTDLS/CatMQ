using NTDLS.PrudentMessageQueueServer;
using NTDLS.PrudentMessageQueueShared.Payloads.Queries.ClientToServer;
using NTDLS.ReliableMessaging;
using System.Net;

namespace NTDLS.PrudentMessageQueue.Server.QueryHandlers
{
    internal class InternalServerQueryHandlers(PMqServer mqServer)
        : IRmMessageHandler
    {
        private readonly PMqServer _mqServer = mqServer;

        public PMqCreateQueueQueryReply CreateQueueQuery(RmContext context, PMqCreateQueueQuery param)
        {
            try
            {
                _mqServer.CreateQueue(param.QueueConfiguration);
                return new PMqCreateQueueQueryReply(true);
            }
            catch (Exception ex)
            {
                return new PMqCreateQueueQueryReply(ex.GetBaseException());
            }
        }

        public PMqDeleteQueueQueryReply DeleteQueueQuery(RmContext context, PMqDeleteQueueQuery param)
        {
            try
            {
                _mqServer.DeleteQueue(param.QueueName);
                return new PMqDeleteQueueQueryReply(true);
            }
            catch (Exception ex)
            {
                return new PMqDeleteQueueQueryReply(ex.GetBaseException());
            }
        }

        public PMqPurgeQueueQueryReply PurgeQueueQuery(RmContext context, PMqPurgeQueueQuery param)
        {
            try
            {
                _mqServer.PurgeQueue(param.QueueName);
                return new PMqPurgeQueueQueryReply(true);
            }
            catch (Exception ex)
            {
                return new PMqPurgeQueueQueryReply(ex.GetBaseException());
            }
        }

        public PMqSubscribeToQueueQueryReply SubscribeToQueueQuery(RmContext context, PMqSubscribeToQueueQuery param)
        {
            try
            {
                _mqServer.SubscribeToQueue(context.ConnectionId,
                    context.TcpClient.Client.LocalEndPoint as IPEndPoint,
                    context.TcpClient.Client.RemoteEndPoint as IPEndPoint,
                    param.QueueName);

                return new PMqSubscribeToQueueQueryReply(true);
            }
            catch (Exception ex)
            {
                return new PMqSubscribeToQueueQueryReply(ex.GetBaseException());
            }
        }

        public PMqUnsubscribeFromQueueQueryReply UnsubscribeFromQueueQuery(RmContext context, PMqUnsubscribeFromQueueQuery param)
        {
            try
            {
                _mqServer.UnsubscribeFromQueue(context.ConnectionId, param.QueueName);
                return new PMqUnsubscribeFromQueueQueryReply(true);
            }
            catch (Exception ex)
            {
                return new PMqUnsubscribeFromQueueQueryReply(ex.GetBaseException());
            }
        }

        public PMqEnqueueMessageToQueueReply EnqueueMessageToQueue(RmContext context, PMqEnqueueMessageToQueue param)
        {
            try
            {
                _mqServer.EnqueueMessage(param.QueueName, param.ObjectType, param.MessageJson);
                return new PMqEnqueueMessageToQueueReply(true);
            }
            catch (Exception ex)
            {
                return new PMqEnqueueMessageToQueueReply(ex.GetBaseException());
            }
        }
    }
}
