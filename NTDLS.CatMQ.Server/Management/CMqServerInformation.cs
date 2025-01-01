using NTDLS.CatMQ.Shared;

namespace NTDLS.CatMQ.Server.Management
{
    /// <summary>
    /// Queue client configuration parameters.
    /// </summary>
    public class CMqServerInformation
    {
        /// <summary>
        /// When true, query replies are queued in a thread pool. Otherwise, queries block other activities.
        /// </summary>
        public bool AsynchronousAcknowledgment { get; internal set; } = true;

        /// <summary>
        /// The default amount of time to wait for a query to reply before throwing a timeout exception.
        /// </summary>
        public int AcknowledgmentTimeoutSeconds { get; internal set; } = CMqDefaults.DEFAULT_ACK_TIMEOUT_SECONDS;
        /// <summary>
        /// The initial size in bytes of the receive buffer.
        /// If the buffer ever gets full while receiving data it will be automatically resized up to MaxReceiveBufferSize.
        /// </summary>
        public int InitialReceiveBufferSize { get; internal set; } = CMqDefaults.DEFAULT_INITIAL_BUFFER_SIZE;

        /// <summary>
        ///The maximum size in bytes of the receive buffer.
        ///If the buffer ever gets full while receiving data it will be automatically resized up to MaxReceiveBufferSize.
        /// </summary>
        public int MaxReceiveBufferSize { get; internal set; } = CMqDefaults.DEFAULT_MAX_BUFFER_SIZE;

        /// <summary>
        ///The growth rate of the auto-resizing for the receive buffer.
        /// </summary>
        public double ReceiveBufferGrowthRate { get; internal set; } = CMqDefaults.DEFAULT_BUFFER_GROWTH_RATE;

        /// <summary>
        /// The TCP/IP port that the message queue server is listening on.
        /// </summary>
        public int ListenPort { get; internal set; }
        /// <summary>
        /// For persistent queues, this is where the data will be stored.
        /// </summary>
        public string? PersistencePath { get; internal set; }
    }
}
