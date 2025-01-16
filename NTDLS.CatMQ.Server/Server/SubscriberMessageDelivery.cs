namespace NTDLS.CatMQ.Server.Server
{
    /// <summary>
    /// Contains information about a delivery of a single message to a single subscriber.
    /// </summary>
    internal class SubscriberMessageDelivery
    {
        public int DeliveryAttemptCount { get; set; }
    }
}
