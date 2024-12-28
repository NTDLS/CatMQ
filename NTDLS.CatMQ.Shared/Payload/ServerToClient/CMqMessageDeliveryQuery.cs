using NTDLS.ReliableMessaging;

namespace NTDLS.CatMQ.Shared.Payloads.Queries.ServerToClient
{
    public class CMqMessageDeliveryQuery(string queueName, string objectType, string messageJson)
    : IRmQuery<CMqMessageDeliveryQueryReply>
    {
        /// <summary>
        /// The name of the queue from which this message is being delivered.
        /// </summary>
        public string QueueName { get; set; } = queueName;
        /// <summary>
        /// The full assembly qualified name of the type of MessageJson.
        /// </summary>
        public string ObjectType { get; set; } = objectType;
        public string MessageJson { get; set; } = messageJson;
    }

    public class CMqMessageDeliveryQueryReply
        : IRmQueryReply
    {
        /// <summary>
        /// Denotes whether the client which received the delivery notification consumed
        /// the message. The message will not leave the top of the queue until it has been
        /// consumed by all subscribers (or the message expires or is dead-lettered).
        /// </summary>
        public bool WasMessageConsumed { get; set; }
        public string? ErrorMessage { get; set; }

        public CMqMessageDeliveryQueryReply(Exception exception)
        {
            WasMessageConsumed = false;
            ErrorMessage = exception.Message;
        }

        public CMqMessageDeliveryQueryReply(bool wasMessageConsumed)
        {
            WasMessageConsumed = wasMessageConsumed;
        }

        public CMqMessageDeliveryQueryReply()
        {
        }
    }
}
