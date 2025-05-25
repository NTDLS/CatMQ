namespace NTDLS.CatMQ.Client
{
    internal class QueueSubscriptionDictionary
        : Dictionary<string, CMqSubscription>
    {
        public QueueSubscriptionDictionary()
            : base(StringComparer.OrdinalIgnoreCase)
        {
        }
    }
}
