namespace NTDLS.PrudentMessageQueueShared
{
    public static class PMqDefaults
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
    }
}
