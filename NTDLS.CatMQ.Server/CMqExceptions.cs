namespace NTDLS.CatMQ.Server
{
    /// <summary>
    /// Thrown when an operation names a queue that does not exist.
    /// </summary>
    public class CMqQueueNotFoundException(string queueName)
        : Exception($"Queue not found: [{queueName}].")
    {
        /// <summary>
        /// The name of the queue that was not found.
        /// </summary>
        public string QueueName { get; } = queueName;
    }

    /// <summary>
    /// Thrown when a message is published to a queue that has reached its MaxDepth.
    /// </summary>
    public class CMqQueueFullException(string queueName, int maxDepth)
        : Exception($"Queue [{queueName}] is full: it has reached its limit of {maxDepth:n0} messages.")
    {
        /// <summary>
        /// The name of the queue that is full.
        /// </summary>
        public string QueueName { get; } = queueName;

        /// <summary>
        /// The most messages that the queue will hold.
        /// </summary>
        public int MaxDepth { get; } = maxDepth;
    }

    /// <summary>
    /// Thrown when a configuration would change the persistence scheme of an existing queue.
    /// </summary>
    public class CMqQueuePersistenceChangeException(string message)
        : ArgumentException(message)
    {
    }

    /// <summary>
    /// Thrown when an operation cannot be performed on a message because it is out for delivery.
    /// </summary>
    public class CMqMessageInDeliveryException(string message)
        : Exception(message)
    {
    }
}
