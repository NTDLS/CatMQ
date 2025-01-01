namespace NTDLS.CatMQ.Client
{
    /// <summary>
    /// Describes a subscription to a queue, this reference is needed to unsubscribe to a single queue subscription.
    /// </summary>
    public class CMqSubscription(string queueName, OnReceivedEvent method)
    {
        /// <summary>
        /// The unique identifier for this subscription.
        /// </summary>
        public Guid Id { get; private set; } = Guid.NewGuid();

        /// <summary>
        /// The name of the queue that this subscription is for.
        /// </summary>
        public string QueueName { get; internal set; } = queueName;

        /// <summary>
        /// Function that is called when a message is received for this subscription.
        /// </summary>
        public OnReceivedEvent Method { get; internal set; } = method;
    }
}
