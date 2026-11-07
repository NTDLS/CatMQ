using NTDLS.CatMQ.Shared;

namespace NTDLS.CatMQ.Client
{
    /// <summary>
    /// Delegate used for server-to-client delivery notifications containing raw JSON.
    /// </summary>
    /// <returns>Return true to mark the message as consumed by the client.</returns>
    public delegate CMqConsumeResult OnMessageReceived(CMqClient client, CMqReceivedMessage rawMessage);

    /// <summary>
    /// Delegate used for asynchronous server-to-client delivery notifications containing raw JSON.
    /// No thread is held while the returned task is pending, so use this when the handler awaits I/O.
    /// </summary>
    /// <returns>The disposition of the message, which is returned to the server once the task completes.</returns>
    public delegate Task<CMqConsumeResult> OnMessageReceivedAsync(CMqClient client, CMqReceivedMessage rawMessage);

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

        /// <summary>
        /// The media type of the body. Defaults to JSON.
        /// </summary>
        public string? ContentType { get; set; }

        /// <summary>
        /// Optional application-defined headers.
        /// </summary>
        public Dictionary<string, string>? Headers { get; set; }

        /// <summary>
        /// Whether the body is base64 encoded binary content.
        /// </summary>
        public bool BodyIsBase64 { get; set; }
    }
}
