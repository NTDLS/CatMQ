namespace NTDLS.CatMQ.Server.Management
{
    /// <summary>
    /// Queue server configuration parameters and various statistics.
    /// </summary>
    public class CMqServerDescriptor
    {
        /// <summary>
        /// The default amount of time to wait for a query to reply before throwing a timeout exception.
        /// </summary>
        public int AcknowledgmentTimeoutSeconds { get; internal set; }

        /// <summary>
        /// The initial size in bytes of the receive buffer.
        /// If the buffer ever gets full while receiving data it will be automatically resized up to MaxReceiveBufferSize.
        /// </summary>
        public int InitialReceiveBufferSize { get; internal set; }

        /// <summary>
        ///The maximum size in bytes of the receive buffer.
        ///If the buffer ever gets full while receiving data it will be automatically resized up to MaxReceiveBufferSize.
        /// </summary>
        public int MaxReceiveBufferSize { get; internal set; }

        /// <summary>
        ///The growth rate of the auto-resizing for the receive buffer.
        /// </summary>
        public double ReceiveBufferGrowthRate { get; internal set; }

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
