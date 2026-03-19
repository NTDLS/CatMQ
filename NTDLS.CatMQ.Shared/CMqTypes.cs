namespace NTDLS.CatMQ.Shared
{
    /// <summary>
    /// Specifies the types of instrumentation events that can occur in the message queue system.
    /// </summary>
    /// <remarks>Use this enumeration to identify and categorize events for monitoring or logging purposes
    /// within the message queue infrastructure. Additional event types may be added in future versions.</remarks>
    public enum CMqInstrumentationEventType
    {
        /// <summary>
        /// Total time take to enqueue messages.
        /// </summary>
        EnqueueMessage,
        /// <summary>
        /// Time taken to acquire read lock on message queues collection during message enqueue.
        /// </summary>
        EnqueueMessageReadLock,
        /// <summary>
        /// Time taken to acquire read lock on message container for persisted storage during message enqueue.
        /// </summary>
        EnqueueMessagePersistenceReadLock,
        /// <summary>
        /// Time taken to acquire write lock on message container for Ephemeral storage during message enqueue.
        /// </summary>
        EnqueueMessageEphemeralWriteLock,
        /// <summary>
        /// Time taken writing a message to the persistence store during message enqueue.
        /// </summary>
        PersistenceStore,
        /// <summary>
        /// Time taken to deliver messages and receive consumption dispositions from subscribers.
        /// </summary>
        DeliverMessageWithResult,
        /// <summary>
        /// Time taken for periodic checkpointing of the message queue persisted data.
        /// </summary>
        PersistenceCheckpoint,
        /// <summary>
        /// Time taken to acquire locks on messages for delivery.
        /// </summary>
        DeliveryMessageLock,
        /// <summary>
        /// Time taken to check for expired messages and handle them accordingly.
        /// </summary>
        ExpirationCheck,
        /// <summary>
        /// Time taken to acquire locks on subscribers for delivery.
        /// </summary>
        DeliverySubscribersLock,
        /// <summary>
        /// Time yielding delivery due to message buffer being empty.
        /// </summary>
        DeliveryYield,
        /// <summary>
        /// Time taken to move messages for dead-lettering.
        /// </summary>
        DeadLetter,
        /// <summary>
        /// Time taken to move removed messages from the persistence store.
        /// </summary>
        PersistenceRemove,
        /// <summary>
        /// Time taken to move removed messages from the message delivery buffer store.
        /// </summary>
        BufferRemove,
        /// <summary>
        /// Time taken to store persistent messages for deferment.
        /// </summary>
        DeferStore,
        /// <summary>
        /// Time taken to acquire lock on messages for deferment.
        /// </summary>
        DeferReadLock,
        /// <summary>
        /// Time taken to acquire write lock on message buffer for hydration from persistence.
        /// </summary>
        MessageBufferHydrationWriteLock,
        /// <summary>
        /// Time taken to fill message buffer for hydration from persistence.
        /// </summary>
        MessageBufferHydration,
        /// <summary>
        /// Time taken to initialize persistent storage during queue startup.
        /// </summary>
        PersistentInitialization,
        /// <summary>
        /// Time taken yielding lock time to avoid deadlocks with other operations such as enqueuing, message expiration, and checkpointing.
        /// </summary>
        DeadlockAvoidance
    }

    /// <summary>
    /// Represents the state of a message in the message queue.
    /// </summary>
    /// <remarks>This enumeration defines the various states a message can be in during its lifecycle within
    /// the message queue. The state indicates whether the message is ready for delivery, actively being delivered, or
    /// requires special handling.</remarks>
    public enum CMqMessageState
    {
        /// <summary>
        /// Message is ready for delivery.
        /// </summary>
        Ready,
        /// <summary>
        /// Message is currently being delivered to subscribers.
        /// </summary>
        OutForDelivery,
        /// <summary>
        /// Message should be dead lettered.
        /// </summary>
        DeadLetter,
        /// <summary>
        /// Message should be dropped.
        /// </summary>
        Drop,
        /// <summary>
        /// The message queue was shutting down and delivery was aborted.
        /// </summary>
        Shutdown
    }

    /// <summary>
    /// Represents the possible dispositions for a message after being processed by a subscriber.
    /// </summary>
    /// <remarks>This enumeration defines how a subscriber can indicate the outcome of processing a message.
    /// The disposition affects whether the message is re-delivered, deferred, or removed from the queue.</remarks>
    public enum CMqConsumptionDisposition
    {
        /// <summary>
        /// The message was not consumed by the subscriber and delivery will not be re-attempted to this subscriber.
        /// </summary>
        NotInterested,
        /// <summary>
        /// The message was not consumed by the subscriber and delivery will be re-attempted to this subscriber.
        /// </summary>
        NotConsumed,
        /// <summary>
        /// The message was consumed by the subscriber.
        /// </summary>
        Consumed,
        /// <summary>
        /// Subscriber requesting that the message be delivered at a later time.
        /// </summary>
        Defer,
        /// <summary>
        /// Subscriber requesting that the message be immediately sent to the dead-letter queue, if any. Otherwise the message is dropped.
        /// Note that the dead-letter queue expiration times are also respected.
        /// </summary>
        DeadLetter,
        /// <summary>
        /// Subscriber requesting that the message be immediately dropped from the queue and cache.
        /// </summary>
        Drop,
        /// <summary>
        /// An exception occurred while delivering or processing the message.
        /// </summary>
        Exception
    }

    /// <summary>
    /// Determines when to remove messages from the queue as they are distributed to subscribers.
    /// </summary>
    public enum CMqConsumptionScheme
    {
        /// <summary>
        /// The messages are delivered to each subscriber, the message is removed once it is delivered to all subscribers and they have consumed or explicitly rejected it.
        /// </summary>
        AllSubscribersSatisfied,
        /// <summary>
        /// The messages are delivered to subscribers, but is removed and delivery is stopped when any one of the subscribers consumes the message.
        /// </summary>
        FirstConsumedSubscriber
    }

    /// <summary>
    /// Determines how messages are distributed to subscribers.
    /// </summary>
    public enum CMqDeliveryScheme
    {
        /// <summary>
        /// Messages are delivered to the subscribers based on the attempted delivery count to each subscriber.
        /// </summary>
        Balanced,
        /// <summary>
        /// Messages are delivered to subscribers in a random order.
        /// </summary>
        Random,
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
