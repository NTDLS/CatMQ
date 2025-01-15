namespace NTDLS.CatMQ.Shared
{
    public enum CMqConsumptionDisposition
    {
        /// <summary>
        /// The message was not consumed by the subscriber.
        /// </summary>
        NotConsumed,
        /// <summary>
        /// The message was consumed by the subscriber.
        /// </summary>
        Consumed,
        /// <summary>
        /// The subscriber is requesting that the message be delivered at a later time.
        /// </summary>
        Defer
    }

    /// <summary>
    /// Determines when to remove messages from the queue as they are distributed to subscribers.
    /// </summary>
    public enum CMqConsumptionScheme
    {
        /// <summary>
        /// The messages are delivered to each subscriber, the message is removed once it is delivered to all subscribers even if they do not consume it.
        /// </summary>
        Delivered,
        /// <summary>
        /// The messages are delivered to each subscriber, but is removed when any one of the subscribers consumes the message.
        /// </summary>
        FirstConsumedSubscriber
    }

    /// <summary>
    /// Determines how messages are distributed to subscribers.
    /// </summary>
    public enum CMqDeliveryScheme
    {
        /// <summary>
        /// Messages are delivered to the subscribers in the order which they were subscribed.
        /// </summary>
        RoundRobbin,
        /// <summary>
        /// Messages are delivered to subscribers in a random order.
        /// </summary>
        Balanced
    }

    /// <summary>
    /// Whether the queue is persisted or ephemeral.
    /// </summary>
    public enum CMqPersistenceScheme
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
    public enum CMqErrorLevel
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
