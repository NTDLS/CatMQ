﻿namespace NTDLS.CatMQ.Shared
{
    public static class CMqDefaults
    {
        /// <summary>
        /// The number of messages to load from a persistence database when hydrating the buffer during delivery.
        /// </summary>
        public const int DEFAULT_PERSISTENT_MESSAGES_BUFFER_SIZE = 1000;

        /// <summary>
        /// The maximum number of messages to keep in memory for persistent queues.
        /// </summary>
        public const int DEFAULT_PERSISTENT_MESSAGES_MAX_BUFFER = 1000;

        /// <summary>
        /// The minimum number of messages to keep in memory for persistent queues.
        /// </summary>
        public const int DEFAULT_PERSISTENT_MESSAGES_MIN_BUFFER = 500;

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
    }
}
