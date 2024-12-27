namespace NTDLS.CatMQServer
{
    /// <summary>
    /// Queue client configuration parameters.
    /// </summary>
    public class CMqServerConfiguration
    {
        /// <summary>
        /// When true, query replies are queued in a thread pool. Otherwise, queries block other activities.
        /// </summary>
        public bool AsynchronousQueryWaiting { get; set; } = true;

        /// <summary>
        /// The default amount of time to wait for a query to reply before throwing a timeout exception.
        /// </summary>
        public TimeSpan QueryTimeout { get; set; } = TimeSpan.FromSeconds(30);
        /// <summary>
        /// The initial size in bytes of the receive buffer.
        /// If the buffer ever gets full while receiving data it will be automatically resized up to MaxReceiveBufferSize.
        /// </summary>
        public int InitialReceiveBufferSize { get; set; } = CatMQShared.CMqDefaults.INITIAL_BUFFER_SIZE;

        /// <summary>
        ///The maximum size in bytes of the receive buffer.
        ///If the buffer ever gets full while receiving data it will be automatically resized up to MaxReceiveBufferSize.
        /// </summary>
        public int MaxReceiveBufferSize { get; set; } = CatMQShared.CMqDefaults.MAX_BUFFER_SIZE;

        /// <summary>
        ///The growth rate of the auto-resizing for the receive buffer.
        /// </summary>
        public double ReceiveBufferGrowthRate { get; set; } = CatMQShared.CMqDefaults.BUFFER_GROWTH_RATE;

        /// <summary>
        /// For persistent queues, this is where the data will be stored.
        /// </summary>
        public string? PersistencePath { get; set; }
    }
}