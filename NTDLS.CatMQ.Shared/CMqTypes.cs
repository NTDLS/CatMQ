namespace NTDLS.CatMQ.Shared
{
    /// <summary>
    /// Determines when to remove messages from the queue as they are distributed to subscribers.
    /// </summary>
    public enum PMqConsumptionScheme
    {
        /// <summary>
        /// The messages are delivered to each subscriber, the message is removed once it is delivered to all subscribers even if they do not consume it.
        /// </summary>
        SuccessfulDeliveryToAllSubscribers,
        /// <summary>
        /// The messages are delivered to each subscriber, but is removed when any one of the subscribers consumes the message.
        /// </summary>
        FirstConsumedSubscriber
    }

    /// <summary>
    /// Determines how messages are distributed to subscribers.
    /// </summary>
    public enum PMqDeliveryScheme
    {
        /// <summary>
        /// Messages are delivered to the subscribers in the order which they were subscribed.
        /// </summary>
        RoundRobbin,
        /// <summary>
        /// Messages are delivered to subscribers in a random order.
        /// </summary>
        Random
    }

    /// <summary>
    /// Whether the queue is persisted or ephemeral.
    /// </summary>
    public enum PMqPersistenceScheme
    {
        /// <summary>
        /// Undelivered messages are lost when the the service stops.
        /// </summary>
        Ephemeral,
        /// <summary>
        /// Messages are stored on disk and survive service restarts.
        /// </summary>
        Persistent
    }

    /// <summary>
    /// Used for message and error logging.
    /// </summary>
    public enum ErrorLevel
    {
        /// <summary>
        /// Use for detailed diagnostic information.
        /// </summary>
        Verbose,
        /// <summary>
        /// Use for debugging information.
        /// </summary>
        Debug,
        /// <summary>
        /// Use for general informational messages.
        /// </summary>
        Information,
        /// <summary>
        /// Use for potentially harmful situations.
        /// </summary>
        Warning,
        /// <summary>
        ///Use for errors that prevent the execution of a specific part of the application.    
        /// </summary>
        Error,
        /// <summary>
        /// Use for critical errors that cause the application to crash or terminate.
        /// </summary>
        Fatal
    }
}
