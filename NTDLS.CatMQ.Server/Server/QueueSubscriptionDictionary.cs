namespace NTDLS.CatMQ.Server.Server
{
    internal class QueueSubscriptionDictionary
        : Dictionary<string, SingleMessageQueueServer>
    {
        public QueueSubscriptionDictionary()
            : base(StringComparer.OrdinalIgnoreCase)
        {
        }
    }
}
