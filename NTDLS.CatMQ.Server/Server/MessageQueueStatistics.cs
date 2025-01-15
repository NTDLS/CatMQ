namespace NTDLS.CatMQ.Server.Server
{
    /// <summary>
    /// Statistics for a single message queue.
    /// </summary>
    internal class MessageQueueStatistics
    {
        /// <summary>
        /// The total number of messages that have been enqueued into this queue.
        /// </summary>
        public ulong ReceivedMessageCount { get; set; }

        /// <summary>
        /// The total number of messages that have been removed from this queue due to age expiration.
        /// </summary>
        public ulong ExpiredMessageCount { get; set; }

        /// <summary>
        /// The total number of messages that have been delivered from this queue to subscribers.
        /// </summary>
        public ulong DeliveredMessageCount { get; set; }

        /// <summary>
        /// The total number of messages that have failed to deliver from this queue to subscribers.
        /// </summary>
        public ulong FailedDeliveryCount { get; set; }

        /// <summary>
        /// The total number of times a subscriber has requested that an attempted delivery be deferred to a later time.
        /// </summary>
        public ulong DeferredDeliveryCount { get; set; }

        /// <summary>
        /// The total number of times a subscriber has requested that message be dropped from the queue.
        /// </summary>
        public ulong ExplicitDropCount { get; set; }

        /// <summary>
        /// The total number of times a subscriber has requested that message be dead-lettered.
        /// </summary>
        public ulong ExplicitDeadLetterCount { get; set; }
    }
}
