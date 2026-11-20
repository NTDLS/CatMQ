namespace NTDLS.CatMQ.Shared
{
    /// <summary>
    /// Provides default configuration values for a message queue system.
    /// </summary>
    /// <remarks>This class contains constants that define default settings for various aspects of the message
    /// queue system, such as buffer sizes, timeout durations, and delivery thread behavior. These values are intended
    /// to be used as sensible defaults but can be overridden in specific implementations if needed.</remarks>
    public static class CMqDefaults
    {
        /// <summary>
        /// the number of delivery cycle attempts to perform before yielding a queues delivery thread.
        /// </summary>
        public const int QUEUE_THREAD_DELIVERY_BURNDOWN = 100;

        /// <summary>
        /// The content type of a message when the publisher does not supply one.
        /// </summary>
        public const string DEFAULT_CONTENT_TYPE = "application/json";

        /// <summary>
        /// How hard to compress frames. Fastest compresses JSON nearly as well as Optimal for much less time.
        /// </summary>
        public const System.IO.Compression.CompressionLevel DEFAULT_COMPRESSION_LEVEL = System.IO.Compression.CompressionLevel.Fastest;

        /// <summary>
        /// Frames smaller than this many bytes are not compressed, because compressing them costs more time than the bytes it saves.
        /// </summary>
        public const int DEFAULT_COMPRESSION_THRESHOLD = 1024;

        /// <summary>
        /// The longest time in milliseconds that a queue will pause deliveries after consecutive delivery failures.
        /// </summary>
        public const int MAX_DELIVERY_FAILURE_BACKOFF_MS = 1000;

        /// <summary>
        /// Amount of time in milliseconds to wait when attmeptig to acquire a lock before timing out and throwing an exception.
        /// </summary>
        public const int DEFAULT_LOCK_TIMEOUT_MS = 10 * 1000;

        /// <summary>
        /// The number of messages to load from a persistence database when hydrating the message buffer.
        /// </summary>
        public const int DEFAULT_PERSISTENT_MESSAGES_BUFFER_SIZE = 5000;

        /// <summary>
        /// The minimum number of messages to keep in memory for persistent queues.
        /// </summary>
        public const int DEFAULT_PERSISTENT_MESSAGES_MIN_BUFFER = 1000;

        /// <summary>
        /// The number of buffered messages at which a persistent queue stops loading more from the database. This limits
        /// memory use when the buffered messages cannot be delivered yet, such as when they are waiting on an idle consumer.
        /// </summary>
        public const int DEFAULT_PERSISTENT_MESSAGES_MAX_BUFFER = 20000;

        /// <summary>
        /// The amount of time to wait when performing deadlock avoidance locks.
        /// </summary>
        public const int DEFAULT_DEADLOCK_AVOIDANCE_WAIT_MS = 1;

        /// <summary>
        /// The amount of time to wait when attempting to acquire a lock.
        /// </summary>
        public const int DEFAULT_TRY_WAIT_MS = 10;

        /// <summary>
        /// The initial size in bytes of the buffer. If the buffer ever gets full while receiving
        /// data it will be automatically resized by a factor of BUFFER_GROWTH_RATE up to MAX_BUFFER_SIZE.
        /// </summary>
        public const int DEFAULT_INITIAL_BUFFER_SIZE = 16 * 1024;

        /// <summary>
        /// The maximum size in bytes of the buffer. If the buffer ever gets full while receiving
        /// data it will be automatically resized by a factor of BUFFER_GROWTH_RATE up to MAX_BUFFER_SIZE.
        /// </summary>
        public const int DEFAULT_MAX_BUFFER_SIZE = 1024 * 1024;

        /// <summary>
        ///The growth rate for auto-resizing the buffer. Expressed in decimal percentages.
        /// </summary>
        public const double DEFAULT_BUFFER_GROWTH_RATE = 0.2;

        /// <summary>
        /// The default amount of time to wait for a query to reply before throwing a timeout exception.
        /// </summary>
        public const int DEFAULT_ACK_TIMEOUT_SECONDS = 30;

        /// <summary>
        /// The port which the queue service will listen on.
        /// </summary>
        public const int DEFAULT_LISTEN_PORT = 45784;

        /// <summary>
        /// Whether or not the client should attempt to reconnect when unexpectedly disconnected.
        /// </summary>
        public const bool DEFAULT_AUTO_RECONNECT = true;

        /// <summary>
        /// Gets or sets a value indicating whether historical statistics are enabled for data collection or analysis.
        /// </summary>
        public const bool DEFAULT_ENABLE_HISTORICAL_STATISTICS = true;

        /// <summary>
        /// The number historical datapoints to retain per queue.
        /// </summary>
        public const int DEFAULT_MAX_HISTORICAL_STATISTICS_DATAPOINTS = 60;
    }
}
