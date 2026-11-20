using CatMQ.Tests.Messages;
using NTDLS.CatMQ.Client;
using NTDLS.CatMQ.Server;
using NTDLS.CatMQ.Shared;
using System.Collections.Concurrent;

namespace CatMQ.Tests.Unit
{
    /// <summary>
    /// Delivery that does not go through the native TCP protocol, using transports that run inside the test.
    /// </summary>
    public class DeliveryTransports(ServerFixture fixture)
        : IClassFixture<ServerFixture>
    {
        private class FakeTransport(Func<CMqReceivedMessage, Task<CMqConsumeResult>>? handler = null)
            : ICMqDeliveryTransport
        {
            private volatile bool _isReady = true;

            public string Name => "fake";
            public bool IsReady { get => _isReady; set => _isReady = value; }
            public ConcurrentQueue<CMqReceivedMessage> Received { get; } = new();

            public Task<CMqConsumeResult> DeliverAsync(CMqReceivedMessage message, CancellationToken cancellationToken)
            {
                Received.Enqueue(message);
                return handler?.Invoke(message) ?? Task.FromResult(CMqConsumeResult.Consumed());
            }
        }

        private static string NewQueueName() => Guid.NewGuid().ToString("N");

        private int Depth(string queueName) => fixture.Server.GetQueue(queueName)?.QueueDepth ?? -1;

        [Theory(DisplayName = "A transport receives a message with its metadata, and its result is honored.")]
        [InlineData(CMqPersistenceScheme.Ephemeral)]
        [InlineData(CMqPersistenceScheme.Persistent)]
        public void TestDelivery(CMqPersistenceScheme persistenceScheme)
        {
            var queueName = NewQueueName();
            fixture.Server.CreateQueue(new CMqQueueConfiguration(queueName) { PersistenceScheme = persistenceScheme });
            fixture.Server.Enqueue(queueName, null, "orders.created", "{\"a\":1}", "application/json",
                new Dictionary<string, string> { ["tenant"] = "acme" });

            var transport = new FakeTransport();
            fixture.Server.AddConsumer(queueName, "web", transport);

            Assert.True(Wait.Until(() => Depth(queueName) == 0));
            var message = Assert.Single(transport.Received);
            Assert.Equal(queueName, message.QueueName);
            Assert.Equal("orders.created", message.MessageType);
            Assert.Equal("{\"a\":1}", message.Body);
            Assert.Equal("acme", message.Headers?["tenant"]);
            Assert.NotEqual(Guid.Empty, message.MessageId);

            var subscriber = Assert.Single(fixture.Server.GetSubscribers(queueName)!);
            Assert.Equal("fake", subscriber.TransportName);
            Assert.Equal("web", subscriber.ConsumerName);
        }

        [Fact(DisplayName = "A transport exception is a failed delivery, and the message is retried.")]
        public void TestTransportFailureIsRetried()
        {
            var queueName = NewQueueName();
            fixture.Server.CreateQueue(new CMqQueueConfiguration(queueName));
            fixture.Server.Enqueue(queueName, null, null, "{}");

            int attempts = 0;
            fixture.Server.AddConsumer(queueName, "web", new FakeTransport(_ => Interlocked.Increment(ref attempts) == 1
                ? throw new Exception("Delivery failed.")
                : Task.FromResult(CMqConsumeResult.Consumed())));

            Assert.True(Wait.Until(() => Depth(queueName) == 0));
            Assert.Equal(2, attempts);
            Assert.Equal(1UL, fixture.Server.GetQueue(queueName)?.FailedDeliveryCount);
        }

        [Fact(DisplayName = "A disposition from a transport is applied to the message.")]
        public void TestDispositionIsApplied()
        {
            var queueName = NewQueueName();
            fixture.Server.CreateQueue(new CMqQueueConfiguration(queueName)
            {
                DeadLetterConfiguration = new CMqDeadLetterQueueConfiguration()
            });
            fixture.Server.Enqueue(queueName, null, null, "{}");

            fixture.Server.AddConsumer(queueName, "web", new FakeTransport(_ =>
                Task.FromResult(new CMqConsumeResult(CMqConsumptionDisposition.DeadLetter))));

            Assert.True(Wait.Until(() => Depth($"{queueName}.dlq") == 1));
            Assert.Equal(0, Depth(queueName));
        }

        [Fact(DisplayName = "Deliveries that have not completed count toward the outstanding limit.")]
        public void TestOutstandingLimitApplies()
        {
            var queueName = NewQueueName();
            fixture.Server.CreateQueue(new CMqQueueConfiguration(queueName) { MaxOutstandingDeliveries = 3 });
            for (int i = 0; i < 10; i++)
            {
                fixture.Server.Enqueue(queueName, null, null, "{}");
            }

            var release = new TaskCompletionSource<CMqConsumeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var transport = new FakeTransport(_ => release.Task);
            fixture.Server.AddConsumer(queueName, "web", transport);

            Assert.True(Wait.Until(() => transport.Received.Count == 3));
            Thread.Sleep(200);
            Assert.Equal(3, transport.Received.Count);
            Assert.Equal(3, fixture.Server.GetQueue(queueName)?.CurrentOutstandingDeliveries);

            release.SetResult(CMqConsumeResult.Consumed());
            Assert.True(Wait.Until(() => Depth(queueName) == 0));
        }

        [Fact(DisplayName = "Messages wait for a consumer that is not ready, without using up delivery attempts.")]
        public void TestNotReadyConsumerWaits()
        {
            var queueName = NewQueueName();
            fixture.Server.CreateQueue(new CMqQueueConfiguration(queueName)
            {
                MaxDeliveryAttempts = 1,
                DeadLetterConfiguration = new CMqDeadLetterQueueConfiguration()
            });
            fixture.Server.Enqueue(queueName, null, null, "{}");

            var transport = new FakeTransport { IsReady = false };
            fixture.Server.AddConsumer(queueName, "poller", transport);

            Thread.Sleep(300);
            Assert.Empty(transport.Received);
            Assert.Equal(1, Depth(queueName));
            Assert.Equal(0, Depth($"{queueName}.dlq"));
            Assert.Equal(0UL, fixture.Server.GetQueue(queueName)?.FailedDeliveryCount);

            transport.IsReady = true;
            Assert.True(Wait.Until(() => Depth(queueName) == 0));
            Assert.Single(transport.Received);
            Assert.Equal(0, Depth($"{queueName}.dlq"));
        }

        [Fact(DisplayName = "A consumer that is not ready does not hold up the others, and still gets its copy.")]
        public void TestNotReadyConsumerDoesNotBlockOthers()
        {
            var queueName = NewQueueName();
            fixture.Server.CreateQueue(new CMqQueueConfiguration(queueName) { ConsumptionScheme = CMqConsumptionScheme.AllSubscribersSatisfied });

            var ready = new FakeTransport();
            var idle = new FakeTransport { IsReady = false };
            fixture.Server.AddConsumer(queueName, "ready", ready);
            fixture.Server.AddConsumer(queueName, "idle", idle);

            for (int i = 0; i < 5; i++)
            {
                fixture.Server.Enqueue(queueName, null, null, $"{{\"n\":{i}}}");
            }

            Assert.True(Wait.Until(() => ready.Received.Count == 5));
            Thread.Sleep(200);
            Assert.Equal(5, ready.Received.Count); //Waiting for the other consumer does not cause redelivery.
            Assert.Empty(idle.Received);
            Assert.Equal(5, Depth(queueName));

            idle.IsReady = true;
            Assert.True(Wait.Until(() => Depth(queueName) == 0));
            Assert.Equal(5, idle.Received.Count);
            Assert.Equal(5, ready.Received.Count);
        }

        [Fact(DisplayName = "A message is finished off when the only consumer it was waiting for is removed.")]
        public void TestMessageFinishesWhenWaitingConsumerIsRemoved()
        {
            var queueName = NewQueueName();
            fixture.Server.CreateQueue(new CMqQueueConfiguration(queueName) { ConsumptionScheme = CMqConsumptionScheme.AllSubscribersSatisfied });

            var ready = new FakeTransport();
            var idle = new FakeTransport { IsReady = false };
            fixture.Server.AddConsumer(queueName, "ready", ready);
            var idleSubscriberId = fixture.Server.AddConsumer(queueName, "idle", idle);

            fixture.Server.Enqueue(queueName, null, null, "{}");
            Assert.True(Wait.Until(() => ready.Received.Count == 1));
            Assert.Equal(1, Depth(queueName));

            fixture.Server.Unsubscribe(idleSubscriberId, queueName);

            Assert.True(Wait.Until(() => Depth(queueName) == 0));
            Assert.Single(ready.Received);
        }

        [Fact(DisplayName = "A message that uses up its delivery attempts is dead-lettered, even with another consumer not ready.")]
        public void TestExhaustedMessageIsFinished()
        {
            var queueName = NewQueueName();
            fixture.Server.CreateQueue(new CMqQueueConfiguration(queueName)
            {
                MaxDeliveryAttempts = 2,
                DeadLetterConfiguration = new CMqDeadLetterQueueConfiguration()
            });
            fixture.Server.Enqueue(queueName, null, null, "{}");

            var failing = new FakeTransport(_ => throw new Exception("Delivery failed."));
            fixture.Server.AddConsumer(queueName, "web", failing);

            Assert.True(Wait.Until(() => Depth($"{queueName}.dlq") == 1));
            Assert.Equal(0, Depth(queueName));
            Assert.Equal(2, failing.Received.Count);
        }

        [Fact(DisplayName = "A TCP subscriber and a transport both get their own copy.")]
        public void TestMixedTransports()
        {
            var queueName = NewQueueName();
            using var client = ClientFactory.CreateAndConnect();
            client.CreateQueue(new CMqQueueConfiguration(queueName) { ConsumptionScheme = CMqConsumptionScheme.AllSubscribersSatisfied });

            int tcpReceived = 0;
            client.Subscribe(queueName, (_, _) => { Interlocked.Increment(ref tcpReceived); return CMqConsumeResult.Consumed(); });
            var transport = new FakeTransport();
            fixture.Server.AddConsumer(queueName, "web", transport);

            for (int i = 0; i < 10; i++)
            {
                client.Enqueue(queueName, new KeyValueMessage(i, "x"));
            }

            Assert.True(Wait.Until(() => Depth(queueName) == 0));
            Assert.Equal(10, tcpReceived);
            Assert.Equal(10, transport.Received.Count);
        }

        [Fact(DisplayName = "A TCP client and a transport with the same name compete as one consumer.")]
        public void TestSharedNameAcrossTransports()
        {
            var queueName = NewQueueName();
            using var publisher = ClientFactory.CreateAndConnect();
            publisher.CreateQueue(new CMqQueueConfiguration(queueName)
            {
                ConsumptionScheme = CMqConsumptionScheme.AllSubscribersSatisfied,
                DeliveryScheme = CMqDeliveryScheme.Balanced
            });

            using var client = new CMqClient(new CMqClientConfiguration { ConsumerName = "workers" });
            client.Connect(Constants.HOST_NAME, Constants.LISTEN_PORT);

            int tcpReceived = 0;
            client.Subscribe(queueName, (_, _) => { Interlocked.Increment(ref tcpReceived); return CMqConsumeResult.Consumed(); });
            var transport = new FakeTransport();
            fixture.Server.AddConsumer(queueName, "workers", transport);

            for (int i = 0; i < 20; i++)
            {
                publisher.Enqueue(queueName, new KeyValueMessage(i, "x"));
            }

            Assert.True(Wait.Until(() => Depth(queueName) == 0));
            Assert.Equal(20, tcpReceived + transport.Received.Count);
            Assert.True(tcpReceived > 0 && transport.Received.Count > 0, $"TCP: {tcpReceived}, transport: {transport.Received.Count}.");
        }

        [Fact(DisplayName = "A removed consumer stops receiving messages.")]
        public void TestRemovedConsumerStopsReceiving()
        {
            var queueName = NewQueueName();
            fixture.Server.CreateQueue(new CMqQueueConfiguration(queueName));

            var transport = new FakeTransport();
            var subscriberId = fixture.Server.AddConsumer(queueName, "web", transport);
            fixture.Server.Enqueue(queueName, null, null, "{}");
            Assert.True(Wait.Until(() => Depth(queueName) == 0));

            fixture.Server.Unsubscribe(subscriberId, queueName);
            fixture.Server.Enqueue(queueName, null, null, "{}");

            Thread.Sleep(200);
            Assert.Single(transport.Received);
            Assert.Equal(1, Depth(queueName));
            Assert.Equal(0, fixture.Server.GetQueue(queueName)?.CurrentSubscriberCount);
        }
    }
}
