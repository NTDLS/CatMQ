namespace NTDLS.CatMQ.Server.Server
{
    /// <summary>
    /// Statistics for a single message queue.
    /// </summary>
    internal class MessageQueueStatistics
    {
        #region LastSerialNumber.

        /// <summary>
        /// Used to keep the messages in order inside the persistence database.
        /// </summary>
        public ulong _lastSerialNumber = 0;

        public ulong GetNextSerialNumber()
            => Interlocked.Increment(ref _lastSerialNumber);

        public void SetLastSerialNumber(ulong value)
            => Interlocked.Exchange(ref _lastSerialNumber, value);

        #endregion

        #region QueueDepth.

        /// <summary>
        /// The number of messages in the queue.
        /// </summary>
        public int QueueDepth => _queueDepth;

        private int _queueDepth = 0;

        public void IncrementQueueDepth()
            => Interlocked.Increment(ref _queueDepth);

        public void DecrementQueueDepth()
            => Interlocked.Decrement(ref _queueDepth);

        public void SetQueueDepth(int value)
            => Interlocked.Exchange(ref _queueDepth, value);

        #endregion

        #region OutstandingDeliveries.

        /// <summary>
        /// The number of asynchronous deliveries that are currently outstanding.
        /// </summary>
        public int OutstandingDeliveries => _outstandingDeliveries;

        private int _outstandingDeliveries = 0;

        public void IncrementOutstandingDeliveries()
            => Interlocked.Increment(ref _outstandingDeliveries);

        public void DecrementOutstandingDeliveries()
            => Interlocked.Decrement(ref _outstandingDeliveries);

        #endregion

        #region ReceivedMessageCount.

        /// <summary>
        /// The total number of messages that have been enqueued into this queue.
        /// </summary>
        public ulong ReceivedMessageCount
        {
            get => _receivedMessageCount;
            set => _receivedMessageCount = value; //Used by deserialization.
        }

        private ulong _receivedMessageCount = 0;
        public void IncrementReceivedMessageCount()
            => Interlocked.Increment(ref _receivedMessageCount);

        #endregion

        #region ExpiredMessageCount.

        /// <summary>
        /// The total number of messages that have been removed from this queue due to age expiration.
        /// </summary>
        public ulong ExpiredMessageCount
        {
            get => _expiredMessageCount;
            set => _expiredMessageCount = value; //Used by deserialization.
        }
        private ulong _expiredMessageCount = 0;
        internal void IncrementExpiredMessageCount()
            => Interlocked.Increment(ref _expiredMessageCount);

        #endregion

        #region DeliveredMessageCount.

        /// <summary>
        /// The total number of messages that have been delivered from this queue to subscribers.
        /// </summary>
        public ulong DeliveredMessageCount
        {
            get => _deliveredMessageCount;
            set => _deliveredMessageCount = value; //Used by deserialization.
        }
        private ulong _deliveredMessageCount = 0;
        internal void IncrementDeliveredMessageCount()
            => Interlocked.Increment(ref _deliveredMessageCount);

        #endregion

        #region FailedDeliveryCount.

        /// <summary>
        /// The total number of messages that have failed to deliver from this queue to subscribers.
        /// </summary>
        public ulong FailedDeliveryCount
        {
            get => _failedDeliveryCount;
            set => _failedDeliveryCount = value; //Used by deserialization.
        }
        private ulong _failedDeliveryCount = 0;
        internal void IncrementFailedDeliveryCount()
            => Interlocked.Increment(ref _failedDeliveryCount);

        #endregion

        #region DeferredDeliveryCount.

        /// <summary>
        /// The total number of times a subscriber has requested that an attempted delivery be deferred to a later time.
        /// </summary>
        public ulong DeferredDeliveryCount
        {
            get => _deferredDeliveryCount;
            set => _deferredDeliveryCount = value; //Used by deserialization.
        }
        private ulong _deferredDeliveryCount = 0;
        internal void IncrementDeferredDeliveryCount()
            => Interlocked.Increment(ref _deferredDeliveryCount);

        #endregion

        #region ExplicitDropCount.

        /// <summary>
        /// The total number of times a subscriber has requested that message be dropped from the queue.
        /// </summary>
        public ulong ExplicitDropCount
        {
            get => _explicitDropCount;
            set => _explicitDropCount = value; //Used by deserialization.
        }
        private ulong _explicitDropCount = 0;
        internal void IncrementExplicitDropCount()
            => Interlocked.Increment(ref _explicitDropCount);

        #endregion

        #region ExplicitDeadLetterCount.

        /// <summary>
        /// The total number of times a subscriber has requested that message be dead-lettered.
        /// </summary>
        public ulong ExplicitDeadLetterCount
        {
            get => _explicitDeadLetterCount;
            set => _explicitDeadLetterCount = value; //Used by deserialization.
        }
        private ulong _explicitDeadLetterCount = 0;
        internal void IncrementExplicitDeadLetterCount()
            => Interlocked.Increment(ref _explicitDeadLetterCount);

        #endregion
    }
}
