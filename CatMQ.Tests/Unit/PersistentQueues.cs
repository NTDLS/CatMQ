using CatMQ.Tests.Messages;
using NTDLS.CatMQ.Shared;
using System.Collections.Concurrent;

namespace CatMQ.Tests.Unit
{
    /// <summary>
    /// The same behaviors as the ephemeral queues, but with messages that live in the RocksDB database.
    /// </summary>
    public class PersistentQueues(ServerFixture fixture)
        : IClassFixture<ServerFixture>
    {
        private static string NewQueueName() => Guid.NewGuid().ToString("N");

        private static CMqQueueConfiguration Persistent(string queueName) => new(queueName)
        {
            PersistenceScheme = CMqPersistenceScheme.Persistent,
            ConsumptionScheme = CMqConsumptionScheme.AllSubscribersSatisfied
        };

        [Fact(DisplayName = "Persistent: enqueue and consume.")]
        public void TestEnqueueAndConsume()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();
            client.CreateQueue(Persistent(queueName));

            var received = new ConcurrentDictionary<int, string>();
            client.Subscribe(queueName, (_, m) =>
            {
                var kv = (KeyValueMessage)m.Deserialize();
                received[kv.Key] = kv.Value;
                return CMqConsumeResult.Consumed();
            });

            for (int i = 0; i < 100; i++)
            {
                client.Enqueue(queueName, new KeyValueMessage(i, $"Value:{i}"));
            }

            Assert.True(Wait.Until(() => received.Count == 100, 30));
            Assert.All(received, kvp => Assert.Equal($"Value:{kvp.Key}", kvp.Value));
            Assert.True(Wait.Until(() => fixture.Server.GetQueue(queueName)?.QueueDepth == 0));
            Assert.Equal(0, fixture.Server.GetQueueMessages(queueName, 0, int.MaxValue)?.Count); //Also gone from the database.
        }

        [Fact(DisplayName = "Persistent: messages are delivered in the order they were enqueued.")]
        public void TestOrdering()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();
            client.CreateQueue(new CMqQueueConfiguration(queueName)
            {
                PersistenceScheme = CMqPersistenceScheme.Persistent,
                AsynchronousDelivery = false //In-order delivery is only guaranteed when deliveries are not in flight simultaneously.
            });

            for (int i = 0; i < 200; i++)
            {
                client.Enqueue(queueName, new KeyValueMessage(i, "x"));
            }

            var order = new ConcurrentQueue<int>();
            client.Subscribe(queueName, (_, m) => { order.Enqueue(((KeyValueMessage)m.Deserialize()).Key); return CMqConsumeResult.Consumed(); });

            Assert.True(Wait.Until(() => order.Count == 200, 30));
            Assert.Equal(Enumerable.Range(0, 200), order);
        }

        [Fact(DisplayName = "Persistent: messages beyond the in-memory buffer are hydrated and delivered.")]
        public void TestHydrationBeyondBuffer()
        {
            int count = CMqDefaults.DEFAULT_PERSISTENT_MESSAGES_BUFFER_SIZE + 1500;

            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();
            client.CreateQueue(Persistent(queueName));

            //Nobody is subscribed yet, so everything is waiting in the database.
            for (int i = 0; i < count; i++)
            {
                client.EnqueueExpedient(queueName, new KeyValueMessage(i, "x"));
            }
            Assert.True(Wait.Until(() => fixture.Server.GetQueue(queueName)?.QueueDepth == count, 60));

            var received = new ConcurrentDictionary<int, byte>();
            client.Subscribe(queueName, (_, m) => { received[((KeyValueMessage)m.Deserialize()).Key] = 1; return CMqConsumeResult.Consumed(); });

            Assert.True(Wait.Until(() => received.Count == count, 90), $"Received {received.Count} of {count}.");
            Assert.True(Wait.Until(() => fixture.Server.GetQueue(queueName)?.QueueDepth == 0));
        }

        [Fact(DisplayName = "Persistent: purge removes messages from the database as well as the buffer.")]
        public void TestPurge()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();
            client.CreateQueue(Persistent(queueName));

            for (int i = 0; i < 100; i++)
            {
                client.Enqueue(queueName, new KeyValueMessage(i, "old"));
            }

            client.PurgeQueue(queueName);

            Assert.Equal(0, fixture.Server.GetQueue(queueName)?.QueueDepth);
            Assert.Equal(0, fixture.Server.GetQueueMessages(queueName, 0, int.MaxValue)?.Count);

            var received = new ConcurrentBag<string>();
            client.Subscribe(queueName, (_, m) => { received.Add(((KeyValueMessage)m.Deserialize()).Value); return CMqConsumeResult.Consumed(); });
            client.Enqueue(queueName, new KeyValueMessage(1, "new"));

            Assert.True(Wait.Until(() => received.Count == 1));
            Thread.Sleep(300);
            Assert.Equal(new[] { "new" }, received.ToArray());
        }

        [Fact(DisplayName = "Persistent: dead-lettering to a persistent dead-letter queue.")]
        public void TestDeadLetter()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();

            var configuration = Persistent(queueName);
            configuration.DeadLetterConfiguration = new() { PersistenceScheme = CMqPersistenceScheme.Persistent };
            client.CreateQueue(configuration);

            client.Subscribe(queueName, (_, _) => CMqConsumeResult.DeadLetter());

            for (int i = 0; i < 25; i++)
            {
                client.Enqueue(queueName, new KeyValueMessage(i, $"Value:{i}"));
            }

            Assert.True(Wait.Until(() => fixture.Server.GetQueue($"{queueName}.dlq")?.QueueDepth == 25));
            Assert.True(Wait.Until(() => fixture.Server.GetQueue(queueName)?.QueueDepth == 0));

            var deadLetters = fixture.Server.GetQueueMessages($"{queueName}.dlq", 0, int.MaxValue);
            Assert.NotNull(deadLetters);
            Assert.Equal(25, deadLetters.Count);
            Assert.Equal(25, deadLetters.Messages.Select(o => o.MessageJson).Distinct().Count());
        }

        [Fact(DisplayName = "Persistent: expired messages are moved to the dead-letter queue.")]
        public void TestExpireToDeadLetter()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();

            var configuration = Persistent(queueName);
            configuration.MaxMessageAge = TimeSpan.FromSeconds(1);
            configuration.DeadLetterConfiguration = new() { PersistenceScheme = CMqPersistenceScheme.Ephemeral };
            client.CreateQueue(configuration);

            //The delivery thread only evaluates expiration for queues that have subscribers.
            using var subscriber = ClientFactory.CreateAndConnect();
            subscriber.Subscribe(queueName, (_, _) => CMqConsumeResult.Defer(TimeSpan.FromMinutes(5)));

            for (int i = 0; i < 10; i++)
            {
                client.Enqueue(queueName, new KeyValueMessage(i, "x"));
            }

            Assert.True(Wait.Until(() => fixture.Server.GetQueue($"{queueName}.dlq")?.QueueDepth == 10, 20));
            Assert.True(Wait.Until(() => fixture.Server.GetQueue(queueName)?.QueueDepth == 0));
            Assert.Equal(10UL, fixture.Server.GetQueue(queueName)?.ExpiredMessageCount);
        }

        [Fact(DisplayName = "Persistent: deferred messages are redelivered.")]
        public void TestDeferAndRedelivery()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();

            var configuration = Persistent(queueName);
            configuration.MaxDeliveryAttempts = 100;
            client.CreateQueue(configuration);

            var consumed = new ConcurrentDictionary<int, int>(); //Key -> deferred count when consumed.
            client.Subscribe(queueName, (_, m) =>
            {
                if (m.DeferredCount < 3)
                {
                    return CMqConsumeResult.Defer(TimeSpan.FromMilliseconds(100));
                }
                consumed[((KeyValueMessage)m.Deserialize()).Key] = m.DeferredCount;
                return CMqConsumeResult.Consumed();
            });

            for (int i = 0; i < 20; i++)
            {
                client.Enqueue(queueName, new KeyValueMessage(i, "x"));
            }

            Assert.True(Wait.Until(() => consumed.Count == 20, 30));
            Assert.All(consumed.Values, count => Assert.Equal(3, count));
        }

        [Fact(DisplayName = "Persistent: purging while deliveries are in flight leaves a consistent queue depth.")]
        public void TestPurgeDuringDelivery()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();
            client.CreateQueue(Persistent(queueName));

            client.Subscribe(queueName, (_, _) => { Thread.Sleep(50); return CMqConsumeResult.Consumed(); });

            for (int i = 0; i < 200; i++)
            {
                client.EnqueueExpedient(queueName, new KeyValueMessage(i, "x"));
            }

            Assert.True(Wait.Until(() => fixture.Server.GetQueue(queueName)?.CurrentOutstandingDeliveries > 0));
            client.PurgeQueue(queueName);

            //Once the in-flight deliveries settle there is nothing left and the depth is not negative or stale.
            Assert.True(Wait.Until(() => fixture.Server.GetQueue(queueName)?.CurrentOutstandingDeliveries == 0, 20));
            Thread.Sleep(300);

            var queue = fixture.Server.GetQueue(queueName);
            Assert.NotNull(queue);
            Assert.Equal(0, queue.QueueDepth);
            Assert.Equal(0, fixture.Server.GetQueueMessages(queueName, 0, int.MaxValue)?.Count);
        }
    }
}
