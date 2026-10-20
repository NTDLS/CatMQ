using CatMQ.Tests.Messages;
using NTDLS.CatMQ.Client;
using NTDLS.CatMQ.Shared;
using System.Collections.Concurrent;

namespace CatMQ.Tests.Unit
{
    /// <summary>
    /// Queue management, dispositions, enqueue options and the client API variants (all on ephemeral queues).
    /// </summary>
    public class QueueOperations(ServerFixture fixture)
        : IClassFixture<ServerFixture>
    {
        private static string NewQueueName() => Guid.NewGuid().ToString("N");

        #region Dispositions.

        [Fact(DisplayName = "Explicit drop removes the message without dead-lettering.")]
        public void TestExplicitDrop()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();

            client.CreateQueue(new CMqQueueConfiguration(queueName)
            {
                DeadLetterConfiguration = new() { PersistenceScheme = CMqPersistenceScheme.Ephemeral }
            });

            int received = 0;
            client.Subscribe(queueName, (_, _) => { Interlocked.Increment(ref received); return CMqConsumeResult.Drop(); });

            for (int i = 0; i < 20; i++)
            {
                client.Enqueue(queueName, new KeyValueMessage(i, "x"));
            }

            Assert.True(Wait.Until(() => fixture.Server.GetQueue(queueName)?.ExplicitDropCount == 20));
            Assert.True(Wait.Until(() => fixture.Server.GetQueue(queueName)?.QueueDepth == 0));
            Assert.Equal(20, received);

            var dlq = fixture.Server.GetQueue($"{queueName}.dlq");
            Assert.NotNull(dlq);
            Assert.Equal(0, dlq.QueueDepth);
        }

        [Fact(DisplayName = "Not-interested subscribers do not keep a message in the queue.")]
        public void TestNotInterested()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();
            client.CreateQueue(new CMqQueueConfiguration(queueName) { ConsumptionScheme = CMqConsumptionScheme.AllSubscribersSatisfied });

            int received = 0;
            client.Subscribe(queueName, (_, _) => { Interlocked.Increment(ref received); return CMqConsumeResult.NotInterested(); });

            for (int i = 0; i < 10; i++)
            {
                client.Enqueue(queueName, new KeyValueMessage(i, "x"));
            }

            Assert.True(Wait.Until(() => fixture.Server.GetQueue(queueName)?.QueueDepth == 0));
            Assert.Equal(10, received); //Each message is offered exactly once, not redelivered.
        }

        [Fact(DisplayName = "All-subscribers-satisfied delivers every message to every subscriber.")]
        public void TestAllSubscribersSatisfied()
        {
            using var subscriber1 = ClientFactory.CreateAndConnect();
            using var subscriber2 = ClientFactory.CreateAndConnect();
            using var publisher = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();

            publisher.CreateQueue(new CMqQueueConfiguration(queueName) { ConsumptionScheme = CMqConsumptionScheme.AllSubscribersSatisfied });

            var received1 = new ConcurrentBag<int>();
            var received2 = new ConcurrentBag<int>();

            subscriber1.Subscribe(queueName, (_, m) => { received1.Add(((KeyValueMessage)m.Deserialize()).Key); return CMqConsumeResult.Consumed(); });
            subscriber2.Subscribe(queueName, (_, m) => { received2.Add(((KeyValueMessage)m.Deserialize()).Key); return CMqConsumeResult.Consumed(); });

            for (int i = 0; i < 30; i++)
            {
                publisher.Enqueue(queueName, new KeyValueMessage(i, "x"));
            }

            Assert.True(Wait.Until(() => received1.Count == 30 && received2.Count == 30));
            Assert.Equal(Enumerable.Range(0, 30), received1.OrderBy(o => o));
            Assert.Equal(Enumerable.Range(0, 30), received2.OrderBy(o => o));
            Assert.True(Wait.Until(() => fixture.Server.GetQueue(queueName)?.QueueDepth == 0));
        }

        [Fact(DisplayName = "First-consumed-subscriber delivers each message to exactly one subscriber.")]
        public void TestFirstConsumedSubscriberDeliversOnce()
        {
            using var subscriber1 = ClientFactory.CreateAndConnect();
            using var subscriber2 = ClientFactory.CreateAndConnect();
            using var publisher = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();

            publisher.CreateQueue(new CMqQueueConfiguration(queueName) { ConsumptionScheme = CMqConsumptionScheme.FirstConsumedSubscriber });

            var received = new ConcurrentBag<int>();
            subscriber1.Subscribe(queueName, (_, m) => { received.Add(((KeyValueMessage)m.Deserialize()).Key); return CMqConsumeResult.Consumed(); });
            subscriber2.Subscribe(queueName, (_, m) => { received.Add(((KeyValueMessage)m.Deserialize()).Key); return CMqConsumeResult.Consumed(); });

            for (int i = 0; i < 40; i++)
            {
                publisher.Enqueue(queueName, new KeyValueMessage(i, "x"));
            }

            Assert.True(Wait.Until(() => received.Count >= 40));
            Thread.Sleep(300); //Give any (incorrect) duplicate deliveries time to show up.
            Assert.Equal(Enumerable.Range(0, 40), received.OrderBy(o => o));
        }

        #endregion

        #region Enqueue options and client API variants.

        [Fact(DisplayName = "Deferred enqueue is not delivered until the defer duration has passed.")]
        public void TestDeferredEnqueue()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();
            client.CreateQueue(queueName);

            DateTime? deliveredAt = null;
            client.Subscribe(queueName, (_, _) => { deliveredAt ??= DateTime.UtcNow; return CMqConsumeResult.Consumed(); });

            var enqueuedAt = DateTime.UtcNow;
            client.Enqueue(queueName, new KeyValueMessage(1, "later"), new CMqEnqueueOptions { DeferDeliveryDuration = TimeSpan.FromSeconds(1.5) });

            Thread.Sleep(500);
            Assert.Null(deliveredAt);

            Assert.True(Wait.Until(() => deliveredAt != null));
            Assert.True(deliveredAt - enqueuedAt >= TimeSpan.FromSeconds(1.4), $"Delivered after {deliveredAt - enqueuedAt}.");
        }

        [Fact(DisplayName = "Expedient enqueue delivers messages.")]
        public void TestExpedientEnqueue()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();
            client.CreateQueue(queueName);

            int received = 0;
            client.Subscribe(queueName, (_, _) => { Interlocked.Increment(ref received); return CMqConsumeResult.Consumed(); });

            for (int i = 0; i < 25; i++)
            {
                client.EnqueueExpedient(queueName, new KeyValueMessage(i, "x"));
            }

            Assert.True(Wait.Until(() => received == 25));
        }

        [Fact(DisplayName = "Pre-serialized enqueue round-trips the type and body.")]
        public void TestPreSerializedEnqueue()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();
            client.CreateQueue(queueName);

            KeyValueMessage? received = null;
            client.Subscribe(queueName, (_, m) => { received = m.Deserialize() as KeyValueMessage; return CMqConsumeResult.Consumed(); });

            client.Enqueue(queueName, typeof(KeyValueMessage).AssemblyQualifiedName!, "{\"Key\":7,\"Value\":\"raw\"}");

            Assert.True(Wait.Until(() => received != null));
            Assert.Equal(7, received!.Key);
            Assert.Equal("raw", received.Value);
        }

        [Fact(DisplayName = "Async API: create, subscribe, enqueue, unsubscribe, purge and delete.")]
        public async Task TestAsyncApi()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();

            await client.CreateQueueAsync(queueName);

            int received = 0;
            await client.SubscribeAsync(queueName, (_, _) => { Interlocked.Increment(ref received); return CMqConsumeResult.Consumed(); });

            for (int i = 0; i < 10; i++)
            {
                await client.EnqueueAsync(queueName, new KeyValueMessage(i, "x"));
            }
            await client.EnqueueExpedientAsync(queueName, new KeyValueMessage(99, "x"));

            Assert.True(Wait.Until(() => received == 11));

            await client.UnsubscribeAsync(queueName);
            Assert.Equal(0, fixture.Server.GetSubscribers(queueName)?.Count);

            await client.EnqueueAsync(queueName, new KeyValueMessage(100, "waiting"));
            Assert.Equal(1, fixture.Server.GetQueue(queueName)?.QueueDepth);

            await client.PurgeQueueAsync(queueName);
            Assert.Equal(0, fixture.Server.GetQueue(queueName)?.QueueDepth);

            await client.DeleteQueueAsync(queueName);
            Assert.Null(fixture.Server.GetQueue(queueName));
        }

        [Fact(DisplayName = "Buffered subscription delivers full batches and flushes the remainder on the interval.")]
        public void TestBufferedSubscription()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();
            client.CreateQueue(queueName);

            var batchSizes = new ConcurrentQueue<int>();
            var keys = new ConcurrentBag<int>();

            client.SubscribeBuffered(queueName, 10, TimeSpan.FromMilliseconds(500), (_, batch) =>
            {
                batchSizes.Enqueue(batch.Count);
                foreach (var message in batch)
                {
                    keys.Add(((KeyValueMessage)message.Deserialize()).Key);
                }
            });

            for (int i = 0; i < 25; i++)
            {
                client.Enqueue(queueName, new KeyValueMessage(i, "x"));
            }

            Assert.True(Wait.Until(() => keys.Count == 25));
            Assert.Equal(Enumerable.Range(0, 25), keys.OrderBy(o => o));
            Assert.True(batchSizes.Count >= 2);
        }

        [Fact(DisplayName = "Two buffered subscriptions on one client each use their own batch size.")]
        public void TestBufferedSubscriptionsAreIndependent()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueA = NewQueueName();
            var queueB = NewQueueName();
            client.CreateQueue(queueA);
            client.CreateQueue(queueB);

            int receivedA = 0, receivedB = 0;
            int wrongSizedBatches = 0;

            client.SubscribeBuffered(queueA, 5, TimeSpan.Zero, (_, batch) =>
            {
                if (batch.Count != 5) Interlocked.Increment(ref wrongSizedBatches);
                Interlocked.Add(ref receivedA, batch.Count);
            });
            client.SubscribeBuffered(queueB, 20, TimeSpan.Zero, (_, batch) =>
            {
                if (batch.Count != 20) Interlocked.Increment(ref wrongSizedBatches);
                Interlocked.Add(ref receivedB, batch.Count);
            });

            for (int i = 0; i < 20; i++)
            {
                client.Enqueue(queueA, new KeyValueMessage(i, "x"));
                client.Enqueue(queueB, new KeyValueMessage(i, "x"));
            }

            Assert.True(Wait.Until(() => receivedA == 20 && receivedB == 20));
            Assert.Equal(0, wrongSizedBatches);
        }

        #endregion

        #region Queue management.

        [Fact(DisplayName = "Purge removes waiting messages and the queue keeps working.")]
        public void TestPurge()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();
            client.CreateQueue(queueName);

            for (int i = 0; i < 50; i++)
            {
                client.Enqueue(queueName, new KeyValueMessage(i, "old"));
            }
            Assert.Equal(50, fixture.Server.GetQueue(queueName)?.QueueDepth);

            client.PurgeQueue(queueName);
            Assert.Equal(0, fixture.Server.GetQueue(queueName)?.QueueDepth);
            Assert.Equal(0, fixture.Server.GetQueueMessages(queueName, 0, int.MaxValue)?.Count);

            var received = new ConcurrentBag<string>();
            client.Subscribe(queueName, (_, m) => { received.Add(((KeyValueMessage)m.Deserialize()).Value); return CMqConsumeResult.Consumed(); });
            client.Enqueue(queueName, new KeyValueMessage(1, "new"));

            Assert.True(Wait.Until(() => received.Count == 1));
            Thread.Sleep(200);
            Assert.Equal("new", received.Single()); //Nothing purged comes back.
        }

        [Fact(DisplayName = "Purging a missing queue fails.")]
        public void TestPurgeMissingQueueFails()
        {
            using var client = ClientFactory.CreateAndConnect();
            Assert.ThrowsAny<Exception>(() => client.PurgeQueue(NewQueueName()));
        }

        [Fact(DisplayName = "Deleted queue is gone and the name can be reused.")]
        public void TestDeleteAndRecreate()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();
            client.CreateQueue(queueName);
            client.Enqueue(queueName, new KeyValueMessage(1, "x"));

            client.DeleteQueue(queueName);

            Assert.Null(fixture.Server.GetQueue(queueName));
            Assert.ThrowsAny<Exception>(() => client.Enqueue(queueName, new KeyValueMessage(2, "x")));

            client.CreateQueue(queueName);
            Assert.Equal(0, fixture.Server.GetQueue(queueName)?.QueueDepth); //The old message did not come back.
        }

        [Fact(DisplayName = "Deleting a missing queue is not an error.")]
        public void TestDeleteMissingQueueIsIdempotent()
        {
            using var client = ClientFactory.CreateAndConnect();
            client.DeleteQueue(NewQueueName());
        }

        [Fact(DisplayName = "Creating an existing queue keeps the original configuration.")]
        public void TestCreateExistingQueueKeepsConfiguration()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();

            client.CreateQueue(new CMqQueueConfiguration(queueName) { MaxDeliveryAttempts = 7 });
            client.CreateQueue(new CMqQueueConfiguration(queueName) { MaxDeliveryAttempts = 3 });

            Assert.Equal(7, fixture.Server.GetQueue(queueName)?.MaxDeliveryAttempts);
        }

        [Fact(DisplayName = "Queue names are case-insensitive.")]
        public void TestQueueNamesAreCaseInsensitive()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = "MixedCase" + NewQueueName();
            client.CreateQueue(queueName);

            int received = 0;
            client.Subscribe(queueName.ToUpperInvariant(), (_, _) => { Interlocked.Increment(ref received); return CMqConsumeResult.Consumed(); });
            client.Enqueue(queueName.ToLowerInvariant(), new KeyValueMessage(1, "x"));

            Assert.True(Wait.Until(() => received == 1));
        }

        [Fact(DisplayName = "Invalid queue names are rejected.")]
        public void TestInvalidQueueNames()
        {
            using var client = ClientFactory.CreateAndConnect();

            Assert.ThrowsAny<Exception>(() => client.CreateQueue($"{NewQueueName()}.dlq"));
            Assert.Throws<ArgumentException>(() => new CMqQueueConfiguration("bad/name"));
            Assert.Throws<ArgumentException>(() => new CMqQueueConfiguration("   "));
            Assert.Throws<ArgumentException>(() => new CMqQueueConfiguration(".."));
        }

        [Fact(DisplayName = "Enqueue to a missing queue fails and does not create it.")]
        public void TestEnqueueToMissingQueue()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();

            Assert.ThrowsAny<Exception>(() => client.Enqueue(queueName, new KeyValueMessage(1, "x")));
            Assert.Null(fixture.Server.GetQueue(queueName));
        }

        #endregion

        #region Subscriptions.

        [Fact(DisplayName = "Subscribing twice to the same queue fails.")]
        public void TestDoubleSubscribeFails()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();
            client.CreateQueue(queueName);

            client.Subscribe(queueName, (_, _) => CMqConsumeResult.Consumed());
            Assert.ThrowsAny<Exception>(() => client.Subscribe(queueName, (_, _) => CMqConsumeResult.Consumed()));
            Assert.Equal(1, fixture.Server.GetSubscribers(queueName)?.Count);
        }

        [Fact(DisplayName = "Unsubscribed clients stop receiving and unsubscribing again fails.")]
        public void TestUnsubscribe()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();
            client.CreateQueue(queueName);

            int received = 0;
            client.Subscribe(queueName, (_, _) => { Interlocked.Increment(ref received); return CMqConsumeResult.Consumed(); });
            client.Enqueue(queueName, new KeyValueMessage(1, "x"));
            Assert.True(Wait.Until(() => received == 1));

            client.Unsubscribe(queueName);
            Assert.Equal(0, fixture.Server.GetSubscribers(queueName)?.Count);
            Assert.ThrowsAny<Exception>(() => client.Unsubscribe(queueName));

            client.Enqueue(queueName, new KeyValueMessage(2, "x"));
            Thread.Sleep(300);
            Assert.Equal(1, received);
            Assert.Equal(1, fixture.Server.GetQueue(queueName)?.QueueDepth); //Waits for a subscriber rather than being lost.
        }

        [Fact(DisplayName = "Disconnecting removes the client's subscriptions from the server.")]
        public void TestDisconnectRemovesSubscriptions()
        {
            var queueName = NewQueueName();

            using var publisher = ClientFactory.CreateAndConnect();
            publisher.CreateQueue(queueName);

            var subscriber = ClientFactory.CreateAndConnect();
            subscriber.Subscribe(queueName, (_, _) => CMqConsumeResult.Consumed());
            Assert.Equal(1, fixture.Server.GetSubscribers(queueName)?.Count);

            subscriber.Disconnect();

            Assert.True(Wait.Until(() => fixture.Server.GetSubscribers(queueName)?.Count == 0));
        }

        [Fact(DisplayName = "Subscribing to a missing queue fails and can be retried after the queue exists.")]
        public void TestSubscribeToMissingQueue()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();

            Assert.ThrowsAny<Exception>(() => client.Subscribe(queueName, (_, _) => CMqConsumeResult.Consumed()));

            client.CreateQueue(queueName);
            client.Subscribe(queueName, (_, _) => CMqConsumeResult.Consumed());
        }

        #endregion

        #region Inspection.

        [Fact(DisplayName = "Queued messages can be listed and read, with paging and truncation.")]
        public void TestQueueMessageInspection()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();
            client.CreateQueue(queueName);

            for (int i = 0; i < 10; i++)
            {
                client.Enqueue(queueName, new KeyValueMessage(i, new string('v', 100)));
            }

            var page = fixture.Server.GetQueueMessages(queueName, 2, 3);
            Assert.NotNull(page);
            Assert.Equal(3, page.Count);
            Assert.Equal(10, page.QueueDepth);

            var first = page.Messages.First();
            Assert.Contains(nameof(KeyValueMessage), first.AssemblyQualifiedTypeName);

            var single = fixture.Server.GetQueueMessage(queueName, first.SerialNumber, truncateToBytes: 20);
            Assert.NotNull(single);
            Assert.Equal(first.SerialNumber, single.SerialNumber);
            Assert.Equal(20, single.MessageJson.Length);

            Assert.ThrowsAny<Exception>(() => fixture.Server.GetQueueMessage(queueName, ulong.MaxValue));
        }

        [Fact(DisplayName = "Queue statistics count received, delivered and subscribers.")]
        public void TestQueueStatistics()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();
            client.CreateQueue(queueName);

            client.Subscribe(queueName, (_, _) => CMqConsumeResult.Consumed());
            for (int i = 0; i < 15; i++)
            {
                client.Enqueue(queueName, new KeyValueMessage(i, "x"));
            }

            Assert.True(Wait.Until(() => fixture.Server.GetQueue(queueName) is { DeliveredMessageCount: 15, QueueDepth: 0, CurrentOutstandingDeliveries: 0 }));

            var queue = fixture.Server.GetQueue(queueName);
            Assert.NotNull(queue);
            Assert.Equal(15UL, queue.ReceivedMessageCount);
            Assert.Equal(1, queue.CurrentSubscriberCount);
            Assert.Equal(0, queue.QueueDepth);
            Assert.Equal(0, queue.CurrentOutstandingDeliveries);
        }

        #endregion
    }
}
