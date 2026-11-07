using NTDLS.CatMQ.Server.Management;
using NTDLS.CatMQ.Shared;

namespace NTDLS.CatMQ.Server.Server
{
    internal class SubscriberDispositions
    {
        /// <summary>
        /// List of consumer IDs that should receive the message.
        /// </summary>
        public HashSet<Guid> SubscriberIDs { get; private set; }

        public HashSet<Guid> RemainingSubscriberIDs { get; private set; }

        /// <summary>
        /// One subscriber for each consumer that still has to receive the message.
        /// </summary>
        public List<CMqSubscriberDescriptor> Remaining { get; private set; }

        /// <summary>
        /// One subscriber for each remaining consumer that can accept a delivery right now.
        /// </summary>
        public List<CMqSubscriberDescriptor> Deliverable { get; private set; }

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
                SubscriberIDs = s.Values.Select(o => o.ConsumerId).ToHashSet();
                RemainingSubscriberIDs = SubscriberIDs.Except(completedIDs).ToHashSet();
                Remaining = s.Values.Where(o => RemainingSubscriberIDs.Contains(o.ConsumerId)).ToList();
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

            //Connections that share a consumer name are competing workers, so only one of them receives each message.
            Deliverable = Remaining.Where(o => o.Transport.IsReady).DistinctBy(o => o.ConsumerId).ToList();
            Remaining = Remaining.DistinctBy(o => o.ConsumerId).ToList();
        }
    }
}
