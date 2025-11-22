using NTDLS.CatMQ.Shared;

namespace NTDLS.CatMQ.Client
{
    /// <summary>
    /// Delegate used for server-to-client delivery notifications containing raw JSON.
    /// </summary>
    /// <returns>Return true to mark the message as consumed by the client.</returns>
    public delegate CMqConsumeResult OnMessageReceived(CMqClient client, CMqReceivedMessage rawMessage);

    /// <summary>
    /// Delegate used for server-to-client delivery notifications containing raw JSON.
    /// </summary>
    public delegate void OnBatchReceived(CMqClient client, List<CMqReceivedMessage> rawMessages);

    /// <summary>
    /// Represents options for configuring message enqueueing behavior, such as delivery delay and server confirmation
    /// timeouts.
    /// </summary>
    public class CMqEnqueueOptions
    {
        /// <summary>
        /// Amount of time, when if set, which the server will delay delivery of the message to subscribers.
        /// </summary>
        public TimeSpan? DeferDeliveryDuration { get; set; }

        /// <summary>
        /// The amount of time to wait on the server for confirmation that it received the message.
        /// </summary>
        public TimeSpan? ServerDeliveryTimeout { get; set; }
    }
}
