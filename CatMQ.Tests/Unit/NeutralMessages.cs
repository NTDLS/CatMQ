using CatMQ.Tests.Messages;
using NTDLS.CatMQ.Client;
using NTDLS.CatMQ.Server;
using NTDLS.CatMQ.Shared;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace CatMQ.Tests.Unit
{
    /// <summary>
    /// Messages that do not depend on .NET type names, and consumers that are identified by name rather than by connection.
    /// </summary>
    public class NeutralMessages(ServerFixture fixture)
        : IClassFixture<ServerFixture>
    {
        public class AliasedMessage : ICMqMessage
        {
            public int Number { get; set; }
        }

        private static string NewQueueName() => Guid.NewGuid().ToString("N");

        private static CMqClient ConnectConsumer(string consumerName)
        {
            var client = new CMqClient(new CMqClientConfiguration { ConsumerName = consumerName });
            client.Connect(Constants.HOST_NAME, Constants.LISTEN_PORT);
            return client;
        }

        [Theory(DisplayName = "A message with a language-neutral type keeps its metadata.")]
        [InlineData(CMqPersistenceScheme.Ephemeral)]
        [InlineData(CMqPersistenceScheme.Persistent)]
        public void TestNeutralMessageMetadata(CMqPersistenceScheme persistenceScheme)
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();
            client.CreateQueue(new CMqQueueConfiguration(queueName) { PersistenceScheme = persistenceScheme });

            client.Enqueue(queueName, "orders.created", JsonSerializer.Serialize(new KeyValueMessage(5, "five")), new CMqEnqueueOptions
            {
                ContentType = "application/vnd.orders+json",
                Headers = new Dictionary<string, string> { ["tenant"] = "acme" }
            });

            var received = new ConcurrentBag<CMqReceivedMessage>();
            client.Subscribe(queueName, (_, m) => { received.Add(m); return CMqConsumeResult.Consumed(); });

            Assert.True(Wait.Until(() => received.Count == 1));
            var message = received.Single();
            Assert.Equal("orders.created", message.MessageType);
            Assert.Equal("application/vnd.orders+json", message.ContentType);
            Assert.Equal("acme", message.Headers?["tenant"]);
            Assert.Equal(5, message.Deserialize<KeyValueMessage>()?.Key);
        }

        [Fact(DisplayName = "A message does not need a type, and defaults to JSON.")]
        public void TestMessageWithoutType()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();
            client.CreateQueue(queueName);

            client.Enqueue(queueName, (string?)null, JsonSerializer.Serialize(new KeyValueMessage(7, "seven")));

            var received = new ConcurrentBag<CMqReceivedMessage>();
            client.Subscribe(queueName, (_, m) => { received.Add(m); return CMqConsumeResult.Consumed(); });

            Assert.True(Wait.Until(() => received.Count == 1));
            var message = received.Single();
            Assert.Null(message.MessageType);
            Assert.Equal(CMqDefaults.DEFAULT_CONTENT_TYPE, message.ContentType);
            Assert.Equal(7, message.Deserialize<KeyValueMessage>()?.Key);
            Assert.ThrowsAny<Exception>(() => message.Deserialize()); //Without a type there is nothing to resolve.
        }

        [Fact(DisplayName = "A registered type name is sent in place of the .NET type, and resolves back to it.")]
        public void TestRegisteredMessageType()
        {
            CMqSerialization.RegisterMessageType<AliasedMessage>("tests.aliased");

            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();
            client.CreateQueue(queueName);

            client.Enqueue(queueName, new AliasedMessage { Number = 9 });

            var received = new ConcurrentBag<CMqReceivedMessage>();
            client.Subscribe(queueName, (_, m) => { received.Add(m); return CMqConsumeResult.Consumed(); });

            Assert.True(Wait.Until(() => received.Count == 1));
            var message = received.Single();
            Assert.Equal("tests.aliased", message.MessageType);
            Assert.Equal(9, Assert.IsType<AliasedMessage>(message.Deserialize()).Number);
        }

        [Fact(DisplayName = "A .NET message still resolves to its own type.")]
        public void TestDotNetMessageStillResolves()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();
            client.CreateQueue(queueName);

            client.Enqueue(queueName, new KeyValueMessage(3, "three"));

            var received = new ConcurrentBag<CMqReceivedMessage>();
            client.Subscribe(queueName, (_, m) => { received.Add(m); return CMqConsumeResult.Consumed(); });

            Assert.True(Wait.Until(() => received.Count == 1));
            var message = received.Single();
            Assert.Equal(3, Assert.IsType<KeyValueMessage>(message.Deserialize()).Key);
            Assert.IsType<KeyValueMessage>(message.Deserialize<ICMqMessage>());
        }

        [Fact(DisplayName = "A binary body survives storage and delivery.")]
        public void TestBinaryBody()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();
            client.CreateQueue(new CMqQueueConfiguration(queueName) { PersistenceScheme = CMqPersistenceScheme.Persistent });

            byte[] bytes = [0, 1, 2, 254, 255];
            client.Enqueue(queueName, "image.thumbnail", Convert.ToBase64String(bytes), new CMqEnqueueOptions
            {
                ContentType = "application/octet-stream",
                BodyIsBase64 = true
            });

            var received = new ConcurrentBag<CMqReceivedMessage>();
            client.Subscribe(queueName, (_, m) => { received.Add(m); return CMqConsumeResult.Consumed(); });

            Assert.True(Wait.Until(() => received.Count == 1));
            Assert.Equal(bytes, received.Single().GetBodyBytes());
        }

        [Fact(DisplayName = "Dead-lettering keeps a message's metadata.")]
        public void TestDeadLetterKeepsMetadata()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();
            client.CreateQueue(new CMqQueueConfiguration(queueName)
            {
                DeadLetterConfiguration = new CMqDeadLetterQueueConfiguration()
            });

            client.Enqueue(queueName, "orders.created", "{}", new CMqEnqueueOptions
            {
                Headers = new Dictionary<string, string> { ["tenant"] = "acme" }
            });
            client.Subscribe(queueName, (_, _) => new CMqConsumeResult(CMqConsumptionDisposition.DeadLetter));

            var received = new ConcurrentBag<CMqReceivedMessage>();
            client.Subscribe($"{queueName}.dlq", (_, m) => { received.Add(m); return CMqConsumeResult.Consumed(); });

            Assert.True(Wait.Until(() => received.Count == 1));
            Assert.Equal("orders.created", received.Single().MessageType);
            Assert.Equal("acme", received.Single().Headers?["tenant"]);
        }

        [Fact(DisplayName = "Messages stored by older versions still load.")]
        public void TestLegacyStoredMessage()
        {
            const string legacyJson = """
                {"QueueName":"q","SerialNumber":12,"MessageId":"00000000-0000-0000-0000-000000000001",
                "Timestamp":"2026-11-01T00:00:00Z","DeferredCount":0,"DeferredUntil":null,"DeferDuration":null,
                "AssemblyQualifiedTypeName":"My.Type, MyAssembly","MessageJson":"{\"Key\":1}"}
                """;

            var type = typeof(CMqServer).Assembly.GetType("NTDLS.CatMQ.Server.Server.EnqueuedMessage")!;
            var message = JsonSerializer.Deserialize(legacyJson, type)!;
            string Read(string name) => (string)type.GetProperty(name)!.GetValue(message)!;

            Assert.Equal("My.Type, MyAssembly", Read("MessageType"));
            Assert.Equal("{\"Key\":1}", Read("Body"));
            Assert.Equal(CMqDefaults.DEFAULT_CONTENT_TYPE, Read("ContentType"));

            //Once rewritten it uses only the new names.
            var rewritten = JsonSerializer.Serialize(message, type);
            Assert.DoesNotContain("AssemblyQualifiedTypeName", rewritten);
            Assert.DoesNotContain("MessageJson", rewritten);
        }

        [Fact(DisplayName = "Connections with the same consumer name compete, and each named consumer gets every message.")]
        public void TestNamedConsumers()
        {
            var queueName = NewQueueName();
            using var publisher = ClientFactory.CreateAndConnect();
            publisher.CreateQueue(new CMqQueueConfiguration(queueName)
            {
                ConsumptionScheme = CMqConsumptionScheme.AllSubscribersSatisfied,
                DeliveryScheme = CMqDeliveryScheme.Balanced
            });

            using var workerA = ConnectConsumer("workers");
            using var workerB = ConnectConsumer("workers");
            using var audit = ConnectConsumer("audit");

            var workerCounts = new ConcurrentDictionary<int, int>();
            var auditCounts = new ConcurrentDictionary<int, int>();
            int workerAReceived = 0, workerBReceived = 0;

            CMqConsumeResult Worker(CMqReceivedMessage message, ref int received)
            {
                Interlocked.Increment(ref received);
                workerCounts.AddOrUpdate(message.Deserialize<KeyValueMessage>()!.Key, 1, (_, c) => c + 1);
                return CMqConsumeResult.Consumed();
            }

            workerA.Subscribe(queueName, (_, m) => Worker(m, ref workerAReceived));
            workerB.Subscribe(queueName, (_, m) => Worker(m, ref workerBReceived));
            audit.Subscribe(queueName, (_, m) =>
            {
                auditCounts.AddOrUpdate(m.Deserialize<KeyValueMessage>()!.Key, 1, (_, c) => c + 1);
                return CMqConsumeResult.Consumed();
            });

            const int count = 40;
            for (int i = 0; i < count; i++)
            {
                publisher.Enqueue(queueName, new KeyValueMessage(i, "x"));
            }

            Assert.True(Wait.Until(() => fixture.Server.GetQueue(queueName)?.QueueDepth == 0));

            Assert.Equal(count, workerCounts.Count);
            Assert.All(workerCounts.Values, c => Assert.Equal(1, c)); //One of the workers, not both.
            Assert.Equal(count, auditCounts.Count);
            Assert.All(auditCounts.Values, c => Assert.Equal(1, c));
            Assert.True(workerAReceived > 0 && workerBReceived > 0, $"Worker A: {workerAReceived}, worker B: {workerBReceived}.");
        }

        [Fact(DisplayName = "Unnamed consumers each get their own copy, as before.")]
        public void TestUnnamedConsumersAreSeparate()
        {
            var queueName = NewQueueName();
            using var publisher = ClientFactory.CreateAndConnect();
            publisher.CreateQueue(new CMqQueueConfiguration(queueName) { ConsumptionScheme = CMqConsumptionScheme.AllSubscribersSatisfied });

            using var first = ClientFactory.CreateAndConnect();
            using var second = ClientFactory.CreateAndConnect();
            int firstCount = 0, secondCount = 0;
            first.Subscribe(queueName, (_, _) => { Interlocked.Increment(ref firstCount); return CMqConsumeResult.Consumed(); });
            second.Subscribe(queueName, (_, _) => { Interlocked.Increment(ref secondCount); return CMqConsumeResult.Consumed(); });

            for (int i = 0; i < 10; i++)
            {
                publisher.Enqueue(queueName, new KeyValueMessage(i, "x"));
            }

            Assert.True(Wait.Until(() => fixture.Server.GetQueue(queueName)?.QueueDepth == 0));
            Assert.Equal(10, firstCount);
            Assert.Equal(10, secondCount);
        }
    }
}
