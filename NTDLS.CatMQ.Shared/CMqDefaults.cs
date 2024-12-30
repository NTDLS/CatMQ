namespace NTDLS.CatMQ.Shared
{
    public static class CMqDefaults
    {
        /// <summary>
        /// The initial size in bytes of the buffer. If the buffer ever gets full while receiving data it will be automatically resized up to MaxBufferSize.
        /// </summary>
        public const int INITIAL_BUFFER_SIZE = 16 * 1024;

        /// <summary>
        ///The maximum size in bytes of the buffer. If the buffer ever gets full while receiving data it will be automatically resized up to MaxBufferSize.
        /// </summary>
        public const int MAX_BUFFER_SIZE = 1024 * 1024;

        /// <summary>
        ///The growth rate of the auto-resizing for the buffer in decimal percentages.
        /// </summary>
        public const double BUFFER_GROWTH_RATE = 0.2;

        /// <summary>
        /// The default amount of time to wait for a query to reply before throwing a timeout exception.
        /// </summary>
        public const int ACK_TIMEOUT_SECONDS = 30;

        /// <summary>
        /// The port which the queue service will listen on.
        /// </summary>
        public const int LISTEN_PORT = 45784;
    }
}
