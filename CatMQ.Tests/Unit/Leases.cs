using NTDLS.CatMQ.Server;
using NTDLS.CatMQ.Shared;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace CatMQ.Tests.Unit
{
    /// <summary>
    /// Consumers that ask for messages, and tell the queue what happened to each one.
    /// </summary>
    public class Leases(ServerFixture fixture)
        : IClassFixture<ServerFixture>
    {
        private static string NewQueueName() => Guid.NewGuid().ToString("N");

        private CMqServer Server => fixture.Server;

        private int Depth(string queueName) => Server.GetQueue(queueName)?.QueueDepth ?? -1;

        private Task<IReadOnlyList<CMqLeasedMessage>> Receive(string queueName, string consumer = "worker", int max = 1, double waitSeconds = 5, double leaseSeconds = 30)
            => Server.ReceiveAsync(queueName, consumer, max, TimeSpan.FromSeconds(waitSeconds), TimeSpan.FromSeconds(leaseSeconds));

        [Theory(DisplayName = "A received message stays in the queue until it is acknowledged.")]
        [InlineData(CMqPersistenceScheme.Ephemeral)]
        [InlineData(CMqPersistenceScheme.Persistent)]
        public async Task TestReceiveAndAcknowledge(CMqPersistenceScheme persistenceScheme)
        {
            var queueName = NewQueueName();
            Server.CreateQueue(new CMqQueueConfiguration(queueName) { PersistenceScheme = persistenceScheme });
            Server.Enqueue(queueName, null, "orders.created", "{\"a\":1}");

            var leased = Assert.Single(await Receive(queueName));
            Assert.Equal("orders.created", leased.Message.MessageType);
            Assert.Equal("{\"a\":1}", leased.Message.Body);
            Assert.Equal(1, leased.Message.DeliveryAttempt);
            Assert.True(leased.LeaseExpiresAt > DateTime.UtcNow);

            Assert.Equal(1, Depth(queueName));
            Assert.Equal(1, Server.GetQueue(queueName)?.CurrentOutstandingDeliveries);

            Assert.Equal(CMqLeaseStatus.Acknowledged, Server.AcknowledgeLease(leased.LeaseId, CMqConsumeResult.Consumed()));

            Assert.True(Wait.Until(() => Depth(queueName) == 0));
            Assert.Equal(0, Server.GetQueue(queueName)?.CurrentOutstandingDeliveries);
        }

        [Fact(DisplayName = "A receive request returns as soon as a message arrives, and empty when none does.")]
        public async Task TestLongPolling()
        {
            var queueName = NewQueueName();
            Server.CreateQueue(new CMqQueueConfiguration(queueName));

            var stopwatch = Stopwatch.StartNew();
            Assert.Empty(await Receive(queueName, waitSeconds: 0.3));
            Assert.InRange(stopwatch.ElapsedMilliseconds, 250, 3000);

            var pending = Receive(queueName, waitSeconds: 30);
            await Task.Delay(100);
            Assert.False(pending.IsCompleted);

            stopwatch.Restart();
            Server.Enqueue(queueName, null, null, "{}");
            var leased = Assert.Single(await pending);
            Assert.True(stopwatch.ElapsedMilliseconds < 2000, $"Took {stopwatch.ElapsedMilliseconds}ms.");
            Server.AcknowledgeLease(leased.LeaseId, CMqConsumeResult.Consumed());
        }

        [Fact(DisplayName = "A request that does not wait can still get a message.")]
        public async Task TestReceiveWithoutWaiting()
        {
            var queueName = NewQueueName();
            Server.CreateQueue(new CMqQueueConfiguration(queueName));
            Server.Enqueue(queueName, null, null, "{}");

            var leased = Assert.Single(await Receive(queueName, waitSeconds: 0));
            Server.AcknowledgeLease(leased.LeaseId, CMqConsumeResult.Consumed());
        }

        [Fact(DisplayName = "An expired lease puts the message back, and a late acknowledgment is refused.")]
        public async Task TestLeaseExpires()
        {
            var queueName = NewQueueName();
            Server.CreateQueue(new CMqQueueConfiguration(queueName));
            Server.Enqueue(queueName, null, null, "{}");

            var first = Assert.Single(await Receive(queueName, leaseSeconds: 1));
            var second = Assert.Single(await Receive(queueName, waitSeconds: 10));

            Assert.Equal(first.Message.MessageId, second.Message.MessageId);
            Assert.Equal(2, second.Message.DeliveryAttempt);
            Assert.Equal(CMqLeaseStatus.Expired, Server.AcknowledgeLease(first.LeaseId, CMqConsumeResult.Consumed()));
            Assert.Equal(1, Depth(queueName)); //The late acknowledgment did not remove the message.
            Assert.Equal(1UL, Server.GetQueue(queueName)?.FailedDeliveryCount);

            Assert.Equal(CMqLeaseStatus.Acknowledged, Server.AcknowledgeLease(second.LeaseId, CMqConsumeResult.Consumed()));
            Assert.True(Wait.Until(() => Depth(queueName) == 0));
        }

        [Fact(DisplayName = "Extending a lease keeps a message past its original expiry.")]
        public async Task TestExtendLease()
        {
            var queueName = NewQueueName();
            Server.CreateQueue(new CMqQueueConfiguration(queueName));
            Server.Enqueue(queueName, null, null, "{}");

            var leased = Assert.Single(await Receive(queueName, leaseSeconds: 1));
            Assert.Equal(CMqLeaseStatus.Active, Server.ExtendLease(leased.LeaseId, TimeSpan.FromSeconds(30), out var expiresAt));
            Assert.True(expiresAt > leased.LeaseExpiresAt);

            await Task.Delay(3000);
            Assert.Equal(0UL, Server.GetQueue(queueName)?.FailedDeliveryCount);
            Assert.Equal(CMqLeaseStatus.Acknowledged, Server.AcknowledgeLease(leased.LeaseId, CMqConsumeResult.Consumed()));
            Assert.True(Wait.Until(() => Depth(queueName) == 0));
        }

        [Fact(DisplayName = "Repeating an acknowledgment is safe, and an unknown lease is reported.")]
        public async Task TestAcknowledgmentIsIdempotent()
        {
            var queueName = NewQueueName();
            Server.CreateQueue(new CMqQueueConfiguration(queueName));
            Server.Enqueue(queueName, null, null, "{}");
            var leased = Assert.Single(await Receive(queueName));

            Assert.Equal(CMqLeaseStatus.Acknowledged, Server.AcknowledgeLease(leased.LeaseId, CMqConsumeResult.Consumed()));
            Assert.Equal(CMqLeaseStatus.Acknowledged, Server.AcknowledgeLease(leased.LeaseId, CMqConsumeResult.Drop()));
            Assert.Equal(CMqLeaseStatus.Acknowledged, Server.ExtendLease(leased.LeaseId, TimeSpan.FromSeconds(1), out _));
            Assert.Equal(CMqLeaseStatus.NotFound, Server.AcknowledgeLease("no-such-lease", CMqConsumeResult.Consumed()));
            Assert.True(Wait.Until(() => Depth(queueName) == 0));
            Assert.Equal(1UL, Server.GetQueue(queueName)?.DeliveredMessageCount);
        }

        [Fact(DisplayName = "An acknowledgment can defer, dead-letter or drop a message.")]
        public async Task TestDispositions()
        {
            var queueName = NewQueueName();
            Server.CreateQueue(new CMqQueueConfiguration(queueName) { DeadLetterConfiguration = new CMqDeadLetterQueueConfiguration() });

            Server.Enqueue(queueName, null, "defer", "{}");
            var deferred = Assert.Single(await Receive(queueName));
            Server.AcknowledgeLease(deferred.LeaseId, CMqConsumeResult.Defer(TimeSpan.FromMilliseconds(500)));
            Assert.Empty(await Receive(queueName, waitSeconds: 0.2));
            var redelivered = Assert.Single(await Receive(queueName));
            Assert.Equal("defer", redelivered.Message.MessageType);
            Assert.Equal(1, redelivered.Message.DeferredCount);
            Server.AcknowledgeLease(redelivered.LeaseId, CMqConsumeResult.DeadLetter());

            Assert.True(Wait.Until(() => Depth($"{queueName}.dlq") == 1));
            Assert.Equal(0, Depth(queueName));

            Server.Enqueue(queueName, null, "drop", "{}");
            Server.AcknowledgeLease(Assert.Single(await Receive(queueName)).LeaseId, CMqConsumeResult.Drop());
            Assert.True(Wait.Until(() => Depth(queueName) == 0));
            Assert.Equal(1, Depth($"{queueName}.dlq"));
        }

        [Fact(DisplayName = "Leased messages count toward the outstanding limit.")]
        public async Task TestOutstandingLimit()
        {
            var queueName = NewQueueName();
            Server.CreateQueue(new CMqQueueConfiguration(queueName) { MaxOutstandingDeliveries = 3 });
            for (int i = 0; i < 10; i++)
            {
                Server.Enqueue(queueName, null, null, "{}");
            }

            var first = await Receive(queueName, max: 10, waitSeconds: 1);
            Assert.Equal(3, first.Count);
            Assert.Empty(await Receive(queueName, max: 10, waitSeconds: 0.3));

            Server.AcknowledgeLease(first[0].LeaseId, CMqConsumeResult.Consumed());
            var next = Assert.Single(await Receive(queueName, max: 10));

            foreach (var lease in first.Skip(1).Append(next))
            {
                Server.AcknowledgeLease(lease.LeaseId, CMqConsumeResult.Consumed());
            }
        }

        [Fact(DisplayName = "A message that keeps expiring is dead-lettered after its delivery attempts run out.")]
        public async Task TestRepeatedExpiryIsDeadLettered()
        {
            var queueName = NewQueueName();
            Server.CreateQueue(new CMqQueueConfiguration(queueName)
            {
                MaxDeliveryAttempts = 2,
                DeadLetterConfiguration = new CMqDeadLetterQueueConfiguration()
            });
            Server.Enqueue(queueName, null, null, "{}");

            Assert.Single(await Receive(queueName, leaseSeconds: 1));
            Assert.Single(await Receive(queueName, waitSeconds: 10, leaseSeconds: 1));

            var poller = Task.Run(async () =>
            {
                while (Depth($"{queueName}.dlq") == 0)
                {
                    await Receive(queueName, waitSeconds: 0.2); //Delivery is what finishes the message off.
                }
            });

            Assert.True(Wait.Until(() => Depth($"{queueName}.dlq") == 1));
            await poller;
            Assert.Equal(0, Depth(queueName));
        }

        [Fact(DisplayName = "Requests with the same consumer name compete, without duplicates.")]
        public async Task TestCompetingRequests()
        {
            var queueName = NewQueueName();
            Server.CreateQueue(new CMqQueueConfiguration(queueName) { MaxOutstandingDeliveries = 8 });

            const int count = 300;
            for (int i = 0; i < count; i++)
            {
                Server.Enqueue(queueName, null, null, $"{{\"n\":{i}}}");
            }

            var seen = new ConcurrentDictionary<string, int>();
            var workers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
            {
                while (seen.Count < count)
                {
                    foreach (var leased in await Receive(queueName, max: 5, waitSeconds: 0.5))
                    {
                        seen.AddOrUpdate(leased.Message.Body, 1, (_, c) => c + 1);
                        Server.AcknowledgeLease(leased.LeaseId, CMqConsumeResult.Consumed());
                    }
                }
            })).ToArray();

            Assert.True(Task.WaitAll(workers, TimeSpan.FromSeconds(30)));
            Assert.Equal(count, seen.Count);
            Assert.All(seen.Values, c => Assert.Equal(1, c));
            Assert.True(Wait.Until(() => Depth(queueName) == 0));
            Assert.Equal(0UL, Server.GetQueue(queueName)?.FailedDeliveryCount);
        }

        [Fact(DisplayName = "A registered consumer gets its copy even when it comes back later.")]
        public async Task TestRegisteredConsumerStaysRegistered()
        {
            var queueName = NewQueueName();
            Server.CreateQueue(new CMqQueueConfiguration(queueName) { ConsumptionScheme = CMqConsumptionScheme.AllSubscribersSatisfied });

            //Both consumers register, then only one of them is around when the message arrives.
            Assert.Empty(await Receive(queueName, "active", waitSeconds: 0));
            Assert.Empty(await Receive(queueName, "offline", waitSeconds: 0));

            Server.Enqueue(queueName, null, null, "{}");

            var active = Assert.Single(await Receive(queueName, "active"));
            Server.AcknowledgeLease(active.LeaseId, CMqConsumeResult.Consumed());
            await Task.Delay(200);
            Assert.Equal(1, Depth(queueName));
            Assert.Empty(await Receive(queueName, "active", waitSeconds: 0.2)); //It already has this one.

            var offline = Assert.Single(await Receive(queueName, "offline"));
            Assert.Equal(active.Message.MessageId, offline.Message.MessageId);
            Server.AcknowledgeLease(offline.LeaseId, CMqConsumeResult.Consumed());
            Assert.True(Wait.Until(() => Depth(queueName) == 0));
        }

        [Fact(DisplayName = "Removing a consumer releases its messages and stops queues waiting for it.")]
        public async Task TestRemoveConsumer()
        {
            var queueName = NewQueueName();
            Server.CreateQueue(new CMqQueueConfiguration(queueName) { ConsumptionScheme = CMqConsumptionScheme.AllSubscribersSatisfied });
            Assert.Empty(await Receive(queueName, "gone", waitSeconds: 0));
            Assert.Empty(await Receive(queueName, "stays", waitSeconds: 0));

            Server.Enqueue(queueName, null, null, "{}");
            var stays = Assert.Single(await Receive(queueName, "stays"));
            Server.AcknowledgeLease(stays.LeaseId, CMqConsumeResult.Consumed());
            var leased = Assert.Single(await Receive(queueName, "gone"));
            Assert.Equal(1, Depth(queueName));

            Assert.True(Server.RemoveLeaseConsumer(queueName, "gone"));
            Assert.False(Server.RemoveLeaseConsumer(queueName, "gone"));

            Assert.Equal(CMqLeaseStatus.Expired, Server.AcknowledgeLease(leased.LeaseId, CMqConsumeResult.Consumed()));
            Assert.Equal(0UL, Server.GetQueue(queueName)?.FailedDeliveryCount);
            Assert.True(Wait.Until(() => Server.GetSubscribers(queueName)?.Count == 1));

            //The remaining consumer already has the message, so a request lets it be finished off.
            Assert.Empty(await Receive(queueName, "stays", waitSeconds: 0.3));
            Assert.True(Wait.Until(() => Depth(queueName) == 0));
        }

        [Fact(DisplayName = "Canceling a request puts its messages back, without using up an attempt.")]
        public async Task TestCanceledRequest()
        {
            var queueName = NewQueueName();
            Server.CreateQueue(new CMqQueueConfiguration(queueName));

            using var cancel = new CancellationTokenSource();
            var pending = Server.ReceiveAsync(queueName, "worker", 1, TimeSpan.FromSeconds(30), null, cancel.Token);
            await Task.Delay(100);
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

            Server.Enqueue(queueName, null, null, "{}");
            var leased = Assert.Single(await Receive(queueName));
            Assert.Equal(1, leased.Message.DeliveryAttempt);
            Server.AcknowledgeLease(leased.LeaseId, CMqConsumeResult.Consumed());
        }

        [Fact(DisplayName = "Requests that time out while messages arrive never lose or penalize a message.")]
        public async Task TestRequestTimeoutRace()
        {
            var queueName = NewQueueName();
            Server.CreateQueue(new CMqQueueConfiguration(queueName) { MaxDeliveryAttempts = 1, DeadLetterConfiguration = new CMqDeadLetterQueueConfiguration() });

            const int count = 200;
            var seen = new ConcurrentDictionary<string, int>();
            var publisher = Task.Run(async () =>
            {
                for (int i = 0; i < count; i++)
                {
                    Server.Enqueue(queueName, null, null, $"{{\"n\":{i}}}");
                    await Task.Delay(1);
                }
            });

            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (seen.Count < count && DateTime.UtcNow < deadline)
            {
                //Each request is as short as allowed, so that many of them end just as a message is being delivered.
                foreach (var leased in await Receive(queueName, waitSeconds: 0))
                {
                    seen.AddOrUpdate(leased.Message.Body, 1, (_, c) => c + 1);
                    Server.AcknowledgeLease(leased.LeaseId, CMqConsumeResult.Consumed());
                }
            }

            await publisher;
            Assert.Equal(count, seen.Count);
            Assert.Equal(0, Depth($"{queueName}.dlq"));
            Assert.Equal(0UL, Server.GetQueue(queueName)?.FailedDeliveryCount);
        }

        [Fact(DisplayName = "Deleting a queue ends waiting requests and outstanding leases.")]
        public async Task TestDeleteQueueWithLeases()
        {
            var queueName = NewQueueName();
            Server.CreateQueue(new CMqQueueConfiguration(queueName));
            Server.Enqueue(queueName, null, null, "{}");
            var leased = Assert.Single(await Receive(queueName));

            var waiting = Receive(queueName, waitSeconds: 30);
            await Task.Delay(100);

            var stopwatch = Stopwatch.StartNew();
            Server.DeleteQueue(queueName);
            Assert.True(stopwatch.ElapsedMilliseconds < 5000, $"Took {stopwatch.ElapsedMilliseconds}ms.");

            Assert.Empty(await waiting.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(CMqLeaseStatus.Expired, Server.AcknowledgeLease(leased.LeaseId, CMqConsumeResult.Consumed()));
        }

        [Fact(DisplayName = "Receiving from a missing queue is an error, and invalid arguments are rejected.")]
        public async Task TestInvalidRequests()
        {
            await Assert.ThrowsAnyAsync<Exception>(() => Receive(NewQueueName()));

            var queueName = NewQueueName();
            Server.CreateQueue(new CMqQueueConfiguration(queueName));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => Server.ReceiveAsync(queueName, " "));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => Server.ReceiveAsync(queueName, "worker", max: 0));
        }

        [Fact(DisplayName = "A persistent queue stops loading messages that are waiting on an idle consumer.")]
        public async Task TestBufferIsLimited()
        {
            var queueName = NewQueueName();
            Server.CreateQueue(new CMqQueueConfiguration(queueName)
            {
                PersistenceScheme = CMqPersistenceScheme.Persistent,
                ConsumptionScheme = CMqConsumptionScheme.AllSubscribersSatisfied,
                MaxOutstandingDeliveries = 64
            });

            Assert.Empty(await Receive(queueName, "idle", waitSeconds: 0));

            int total = CMqDefaults.DEFAULT_PERSISTENT_MESSAGES_MAX_BUFFER + 10000;
            for (int i = 0; i < total; i++)
            {
                Server.Enqueue(queueName, null, null, "{}");
            }

            int delivered = 0;
            Server.AddConsumer(queueName, "fast", new CountingTransport(() => Interlocked.Increment(ref delivered)));

            //It gets everything that can be buffered, and then has to wait for the idle consumer.
            Assert.True(Wait.Until(() => Volatile.Read(ref delivered) >= CMqDefaults.DEFAULT_PERSISTENT_MESSAGES_MAX_BUFFER, 60));
            await Task.Delay(500);
            int settled = Volatile.Read(ref delivered);
            await Task.Delay(500);

            Assert.Equal(settled, Volatile.Read(ref delivered));
            Assert.True(settled < total, $"Delivered {settled} of {total}.");

            Server.DeleteQueue(queueName);
        }

        private class CountingTransport(Action onDelivery) : ICMqDeliveryTransport
        {
            public string Name => "counting";

            public Task<CMqConsumeResult> DeliverAsync(CMqReceivedMessage message, CancellationToken cancellationToken)
            {
                onDelivery();
                return Task.FromResult(CMqConsumeResult.Consumed());
            }
        }
    }
}
