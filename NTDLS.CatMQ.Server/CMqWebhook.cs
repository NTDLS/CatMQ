namespace NTDLS.CatMQ.Server
{
    /// <summary>
    /// How CatMQ delivers a queue's messages to a URL.
    /// </summary>
    public class CMqWebhookSettings
    {
        /// <summary>
        /// The URL that each message is POSTed to. It must be https, unless the server allows http with AllowHttpWebhooks.
        /// </summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// How long to wait for the response before the delivery counts as failed.
        /// </summary>
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// The most deliveries in flight to this webhook at once. Null leaves it to the queue's MaxOutstandingDeliveries.
        /// </summary>
        public int? MaxConcurrency { get; set; }

        /// <summary>
        /// Extra headers to send with each delivery, such as credentials for the receiving service.
        /// </summary>
        public Dictionary<string, string>? Headers { get; set; }

        /// <summary>
        /// A disabled webhook receives nothing, and messages wait for it as they would for any consumer that is not ready.
        /// </summary>
        public bool Enabled { get; set; } = true;
    }

    /// <summary>
    /// A webhook and how its deliveries have been going.
    /// </summary>
    public class CMqWebhookDescriptor
    {
        /// <summary>
        /// The name of the webhook, which is also its consumer name.
        /// </summary>
        public string Name { get; internal set; } = string.Empty;

        /// <summary>
        /// The queue that it receives from.
        /// </summary>
        public string QueueName { get; internal set; } = string.Empty;

        /// <summary>
        /// How it is delivered to.
        /// </summary>
        public CMqWebhookSettings Settings { get; internal set; } = new();

        /// <summary>
        /// When it last responded with a result: consumed, dropped, dead-lettered or deferred.
        /// </summary>
        public DateTime? LastSuccessAt { get; internal set; }

        /// <summary>
        /// When a delivery to it last failed.
        /// </summary>
        public DateTime? LastFailureAt { get; internal set; }

        /// <summary>
        /// Why a delivery to it last failed.
        /// </summary>
        public string? LastFailure { get; internal set; }

        /// <summary>
        /// How many deliveries have failed since the last one that did not.
        /// </summary>
        public int ConsecutiveFailures { get; internal set; }
    }
}
