using NTDLS.CatMQ.Shared;

namespace NTDLS.CatMQ.Server
{
    /// <summary>
    /// Queue client configuration parameters.
    /// </summary>
    public class CMqServerConfiguration
    {
        /// <summary>
        /// Gets or sets a value indicating whether instrumentation is enabled.
        /// </summary>
        public bool EnableInstrumentation { get; set; } = false;
        /// <summary>
        /// The default amount of time to wait for a query to reply before throwing a timeout exception.
        /// </summary>
        public int AcknowledgmentTimeoutSeconds { get; set; } = CMqDefaults.DEFAULT_ACK_TIMEOUT_SECONDS;
        /// <summary>
        /// The initial size in bytes of the receive buffer.
        /// If the buffer ever gets full while receiving data it will be automatically resized up to MaxReceiveBufferSize.
        /// </summary>
        public int InitialReceiveBufferSize { get; set; } = CMqDefaults.DEFAULT_INITIAL_BUFFER_SIZE;

        /// <summary>
        ///The maximum size in bytes of the receive buffer.
        ///If the buffer ever gets full while receiving data it will be automatically resized up to MaxReceiveBufferSize.
        /// </summary>
        public int MaxReceiveBufferSize { get; set; } = CMqDefaults.DEFAULT_MAX_BUFFER_SIZE;

        /// <summary>
        ///The growth rate of the auto-resizing for the receive buffer.
        /// </summary>
        public double ReceiveBufferGrowthRate { get; set; } = CMqDefaults.DEFAULT_BUFFER_GROWTH_RATE;

        /// <summary>
        /// For persistent queues, this is where the data will be stored.
        /// </summary>
        public string? PersistencePath { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether historical statistics are enabled for data collection or analysis.
        /// </summary>
        public bool EnableHistoricalStatistics { get; set; } = CMqDefaults.DEFAULT_ENABLE_HISTORICAL_STATISTICS;

        /// <summary>
        /// The number historical datapoints to retain per queue.
        /// </summary>
        public int MaxHistoricalStatisticsDatapoints { get; set; } = CMqDefaults.DEFAULT_MAX_HISTORICAL_STATISTICS_DATAPOINTS;

        /// <summary>
        /// How hard to compress frames that are at least CompressionThreshold bytes. Use NoCompression to turn compression off.
        /// The other side reads frames whatever its own setting is.
        /// </summary>
        public System.IO.Compression.CompressionLevel CompressionLevel { get; set; } = CMqDefaults.DEFAULT_COMPRESSION_LEVEL;

        /// <summary>
        /// Frames smaller than this many bytes are not compressed, because compressing them costs more time than the bytes it saves.
        /// </summary>
        public int CompressionThreshold { get; set; } = CMqDefaults.DEFAULT_COMPRESSION_THRESHOLD;

        /// <summary>
        /// Whether to send small writes immediately rather than holding them back to combine them (Nagle's algorithm), which
        /// delays messages and replies when several are in flight on one connection.
        /// </summary>
        public bool NoDelay { get; set; } = CMqDefaults.DEFAULT_NO_DELAY;
    }
}