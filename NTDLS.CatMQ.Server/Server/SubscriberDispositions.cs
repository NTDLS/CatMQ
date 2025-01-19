using NTDLS.CatMQ.Server.Management;
using NTDLS.CatMQ.Shared;

namespace NTDLS.CatMQ.Server.Server
{
    internal class SubscriberDispositions
    {
        public HashSet<Guid> IDs { get; private set; }
        public HashSet<Guid> RemainingIDs { get; private set; }
        public List<CMqSubscriberDescriptor> Remaining { get; private set; }
        public HashSet<Guid> ConsumedIDs { get; private set; }

        public SubscriberDispositions(SingleMessageQueueServer messageQueue, EnqueuedMessage message)
        {
            var completedIDs = message.FailedDeliverySubscriberIDs.Union(message.SatisfiedDeliverySubscriberIDs).ToHashSet();
            ConsumedIDs = message.ConsumedDeliverySubscriberIDs.ToHashSet();

            messageQueue.Subscribers.Read(s =>
            {
                IDs = s.Keys.ToHashSet();
                RemainingIDs = s.Keys.Except(completedIDs).ToHashSet();
                Remaining = s.Where(o => RemainingIDs.Contains(o.Key)).Select(o => o.Value).ToList();
            });

            IDs ??= new();
            RemainingIDs ??= new();
            Remaining ??= new();

            if (messageQueue.Configuration.DeliveryScheme == CMqDeliveryScheme.Balanced)
            {
                Remaining = Remaining.OrderBy(_ => Guid.NewGuid()).ToList();
            }
        }
    }
}
