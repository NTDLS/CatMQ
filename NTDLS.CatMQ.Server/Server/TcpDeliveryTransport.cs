using NTDLS.CatMQ.Shared;
using NTDLS.CatMQ.Shared.Payload.ServerToClient;

namespace NTDLS.CatMQ.Server.Server
{
    /// <summary>
    /// Delivers messages to a subscriber that is connected with the native TCP protocol.
    /// </summary>
    internal class TcpDeliveryTransport(CMqServer queueServer, Guid connectionId)
        : ICMqDeliveryTransport
    {
        public string Name => "tcp";

        public Task<CMqConsumeResult> DeliverAsync(CMqReceivedMessage message, CancellationToken cancellationToken)
        {
            return queueServer.QueryConnectionAsync(connectionId, new CMqMessageDeliveryQuery(message.QueueName, message.SerialNumber, message.MessageType, message.Body)
            {
                MessageId = message.MessageId,
                ContentType = message.ContentType,
                Headers = message.Headers,
                BodyIsBase64 = message.BodyIsBase64,
                DeliveryAttempt = message.DeliveryAttempt,
                Timestamp = message.Timestamp,
                DeferDuration = message.DeferDuration,
                DeferredCount = message.DeferredCount,
                SubscriberDeliveryCount = message.SubscriberDeliveryCount,
                SatisfiedSubscriberCount = message.SatisfiedSubscriberCount,
                FailedSubscriberCount = message.FailedSubscriberCount
            });
        }
    }
}
