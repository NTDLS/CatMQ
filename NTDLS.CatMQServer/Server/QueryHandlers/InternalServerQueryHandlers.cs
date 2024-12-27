using NTDLS.CatMQServer;
using NTDLS.CatMQShared.Payloads.Queries.ClientToServer;
using NTDLS.ReliableMessaging;
using System.Net;

namespace NTDLS.CatMQ.Server.QueryHandlers
{
    internal class InternalServerQueryHandlers(CMqServer mqServer)
        : IRmMessageHandler
    {
        private readonly CMqServer _mqServer = mqServer;

        public CMqCreateQueueQueryReply CreateQueueQuery(RmContext context, CMqCreateQueueQuery param)
        {
            try
            {
                _mqServer.CreateQueue(param.QueueConfiguration);
                return new CMqCreateQueueQueryReply(true);
            }
            catch (Exception ex)
            {
                return new CMqCreateQueueQueryReply(ex.GetBaseException());
            }
        }

        public CMqDeleteQueueQueryReply DeleteQueueQuery(RmContext context, CMqDeleteQueueQuery param)
        {
            try
            {
                _mqServer.DeleteQueue(param.QueueName);
                return new CMqDeleteQueueQueryReply(true);
            }
            catch (Exception ex)
            {
                return new CMqDeleteQueueQueryReply(ex.GetBaseException());
            }
        }

        public CMqPurgeQueueQueryReply PurgeQueueQuery(RmContext context, CMqPurgeQueueQuery param)
        {
            try
            {
                _mqServer.PurgeQueue(param.QueueName);
                return new CMqPurgeQueueQueryReply(true);
            }
            catch (Exception ex)
            {
                return new CMqPurgeQueueQueryReply(ex.GetBaseException());
            }
        }

        public CMqSubscribeToQueueQueryReply SubscribeToQueueQuery(RmContext context, CMqSubscribeToQueueQuery param)
        {
            try
            {
                _mqServer.SubscribeToQueue(context.ConnectionId,
                    context.TcpClient.Client.LocalEndPoint as IPEndPoint,
                    context.TcpClient.Client.RemoteEndPoint as IPEndPoint,
                    param.QueueName);

                return new CMqSubscribeToQueueQueryReply(true);
            }
            catch (Exception ex)
            {
                return new CMqSubscribeToQueueQueryReply(ex.GetBaseException());
            }
        }

        public CMqUnsubscribeFromQueueQueryReply UnsubscribeFromQueueQuery(RmContext context, CMqUnsubscribeFromQueueQuery param)
        {
            try
            {
                _mqServer.UnsubscribeFromQueue(context.ConnectionId, param.QueueName);
                return new CMqUnsubscribeFromQueueQueryReply(true);
            }
            catch (Exception ex)
            {
                return new CMqUnsubscribeFromQueueQueryReply(ex.GetBaseException());
            }
        }

        public CMqEnqueueMessageToQueueReply EnqueueMessageToQueue(RmContext context, CMqEnqueueMessageToQueue param)
        {
            try
            {
                _mqServer.EnqueueMessage(param.QueueName, param.ObjectType, param.MessageJson);
                return new CMqEnqueueMessageToQueueReply(true);
            }
            catch (Exception ex)
            {
                return new CMqEnqueueMessageToQueueReply(ex.GetBaseException());
            }
        }
    }
}
