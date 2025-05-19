using NTDLS.CatMQ.Shared;

namespace NTDLS.CatMQ.Server
{
    /// <summary>
    /// Queue client configuration parameters.
    /// </summary>
    public class CMqServerConfiguration
    {
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
    }
}