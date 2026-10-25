using CatMQ.Tests.Messages;
using NTDLS.CatMQ.Shared;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace CatMQ.Tests.Unit
{
    /// <summary>
    /// Subscriptions whose handler returns Task&lt;CMqConsumeResult&gt;.
    /// </summary>
    public class AsyncSubscriptions(ServerFixture fixture)
        : IClassFixture<ServerFixture>
    {
        private static string NewQueueName() => Guid.NewGuid().ToString("N");

        [Fact(DisplayName = "Async handler consumes messages (Subscribe).")]
        public void TestSubscribe()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();
            client.CreateQueue(new CMqQueueConfiguration(queueName));

            var received = new ConcurrentBag<int>();
            client.Subscribe(queueName, async (_, m) =>
            {
                await Task.Delay(10);
                received.Add(((KeyValueMessage)m.Deserialize()).Key);
                return CMqConsumeResult.Consumed();
            });

            for (int i = 0; i < 25; i++)
            {
                client.Enqueue(queueName, new KeyValueMessage(i, "x"));
            }

            Assert.True(Wait.Until(() => fixture.Server.GetQueue(queueName)?.QueueDepth == 0));
            Assert.Equal(Enumerable.Range(0, 25), received.OrderBy(o => o));
        }

        [Fact(DisplayName = "Async handler consumes messages (SubscribeAsync).")]
        public async Task TestSubscribeAsync()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();
            await client.CreateQueueAsync(new CMqQueueConfiguration(queueName));

            int received = 0;
            await client.SubscribeAsync(queueName, async (_, _) =>
            {
                await Task.Yield();
                Interlocked.Increment(ref received);
                return CMqConsumeResult.Consumed();
            });

            for (int i = 0; i < 10; i++)
            {
                await client.EnqueueAsync(queueName, new KeyValueMessage(i, "x"));
            }

            Assert.True(Wait.Until(() => fixture.Server.GetQueue(queueName)?.QueueDepth == 0));
            Assert.Equal(10, received);
        }

        [Fact(DisplayName = "Async handler dispositions are honored.")]
        public void TestDisposition()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();
            client.CreateQueue(new CMqQueueConfiguration(queueName));

            client.Subscribe(queueName, async (_, _) =>
            {
                await Task.Delay(10);
                return CMqConsumeResult.Drop();
            });

            for (int i = 0; i < 10; i++)
            {
                client.Enqueue(queueName, new KeyValueMessage(i, "x"));
            }

            Assert.True(Wait.Until(() => fixture.Server.GetQueue(queueName)?.ExplicitDropCount == 10));
            Assert.True(Wait.Until(() => fixture.Server.GetQueue(queueName)?.QueueDepth == 0));
        }

        [Fact(DisplayName = "Async handler exceptions count as failed deliveries.")]
        public void TestException()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();
            client.CreateQueue(new CMqQueueConfiguration(queueName) { MaxDeliveryAttempts = 2 });

            int attempts = 0;
            client.Subscribe(queueName, async (_, _) =>
            {
                await Task.Delay(10);
                Interlocked.Increment(ref attempts);
                throw new Exception("Some async error");
            });

            client.Enqueue(queueName, new KeyValueMessage(1, "x"));

            Assert.True(Wait.Until(() => fixture.Server.GetQueue(queueName)?.FailedDeliveryCount >= 2));
            Assert.True(Wait.Until(() => fixture.Server.GetQueue(queueName)?.QueueDepth == 0));
            Assert.True(attempts >= 2);
        }

        /// <summary>
        /// Pending async handlers must not hold threads, otherwise slow handlers starve the thread-pool and deliveries stall.
        /// </summary>
        [Fact(DisplayName = "Pending async handlers do not block threads.")]
        public void TestDoesNotBlock()
        {
            const int messageCount = 300;
            const int delayMs = 1000;

            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();
            client.CreateQueue(new CMqQueueConfiguration(queueName) { MaxOutstandingDeliveries = messageCount });

            for (int i = 0; i < messageCount; i++)
            {
                client.Enqueue(queueName, new KeyValueMessage(i, "x"));
            }

            int concurrent = 0;
            int maxConcurrent = 0;
            int received = 0;

            var sw = Stopwatch.StartNew();
            client.Subscribe(queueName, async (_, _) =>
            {
                var now = Interlocked.Increment(ref concurrent);
                int max;
                while (now > (max = Volatile.Read(ref maxConcurrent)) && Interlocked.CompareExchange(ref maxConcurrent, now, max) != max)
                {
                }

                await Task.Delay(delayMs);

                Interlocked.Decrement(ref concurrent);
                Interlocked.Increment(ref received);
                return CMqConsumeResult.Consumed();
            });

            Assert.True(Wait.Until(() => received == messageCount, 30), $"Only {received} of {messageCount} were received.");
            sw.Stop();

            Assert.True(maxConcurrent > messageCount / 2, $"Only {maxConcurrent} of {messageCount} handlers ran concurrently.");
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"{messageCount} concurrent {delayMs}ms handlers took {sw.Elapsed.TotalSeconds:n1}s.");
        }
    }
}
