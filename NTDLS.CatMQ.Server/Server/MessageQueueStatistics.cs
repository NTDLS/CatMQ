namespace NTDLS.CatMQ.Server.Server
{
    /// <summary>
    /// Statistics for a single message queue.
    /// </summary>
    internal class MessageQueueStatistics
    {
        /// <summary>
        /// The number of messages in the queue.
        /// </summary>
        public int QueueDepth
        {
            get => _queueDepth;
        }

        /// <summary>
        /// Used to keep the messages in order inside the persistence database.
        /// </summary>
        public ulong _lastSerialNumber = 0;

        public ulong GetNextSerialNumber()
            => Interlocked.Increment(ref _lastSerialNumber);

        public void SetLastSerialNumber(ulong value)
            => Interlocked.Exchange(ref _lastSerialNumber, value);

        private int _queueDepth = 0;
        public void IncrementQueueDepth()
            => Interlocked.Increment(ref _queueDepth);

        public void DecrementQueueDepth()
            => Interlocked.Decrement(ref _queueDepth);

        public void SetQueueDepth(int value)
            => Interlocked.Exchange(ref _queueDepth, value);

        /// <summary>
        /// The total number of messages that have been enqueued into this queue.
        /// </summary>
        public ulong ReceivedMessageCount
        {
            get => _receivedMessageCount;
            set => _receivedMessageCount = value;
        }

        private ulong _receivedMessageCount = 0;
        public void IncrementReceivedMessageCount()
            => Interlocked.Increment(ref _receivedMessageCount);

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
