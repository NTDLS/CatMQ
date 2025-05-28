using NTDLS.CatMQ.Server.Management;
using NTDLS.CatMQ.Shared;

namespace NTDLS.CatMQ.Server.Server
{
    internal class SubscriberDispositions
    {
        /// <summary>
        /// List of subscriber IDs that should receive the message.
        /// </summary>
        public HashSet<Guid> SubscriberIDs { get; private set; }

        public HashSet<Guid> RemainingSubscriberIDs { get; private set; }

        public List<CMqSubscriberDescriptor> Remaining { get; private set; }

        /// <summary>
        /// The ID of the subscribers that have replied with a "consumed" disposition.
        /// </summary>
        public HashSet<Guid> ConsumedSubscriberIDs { get; private set; }

        public SubscriberDispositions(SingleMessageQueueServer messageQueue, EnqueuedMessage message)
        {
            var completedIDs = message.DeliveryLimitReachedSubscriberIDs.Union(message.SatisfiedDeliverySubscriberIDs).ToHashSet();

            ConsumedSubscriberIDs = message.ConsumedDeliverySubscriberIDs.ToHashSet();

            messageQueue.Subscribers.Read(s =>
            {
                SubscriberIDs = s.Keys.ToHashSet();
                RemainingSubscriberIDs = s.Keys.Except(completedIDs).ToHashSet();
                Remaining = s.Where(o => RemainingSubscriberIDs.Contains(o.Key)).Select(o => o.Value).ToList();
            });

            SubscriberIDs ??= new();
            RemainingSubscriberIDs ??= new();
            Remaining ??= new();

            if (messageQueue.Configuration.DeliveryScheme == CMqDeliveryScheme.Random)
            {
                Remaining = Remaining.OrderBy(_ => Guid.NewGuid()).ToList();
            }
            else if (messageQueue.Configuration.DeliveryScheme == CMqDeliveryScheme.Balanced)
            {
                Remaining = Remaining.OrderBy(o => o.AttemptedDeliveryCount).ToList();
            }
        }
    }
}
