using CatMQ.Tests.Messages;
using NTDLS.CatMQ.Server;
using NTDLS.CatMQ.Shared;

namespace CatMQ.Tests.Unit
{
    /// <summary>
    /// Queues that refuse new messages once they hold as many as they are allowed.
    /// </summary>
    public class DepthLimits(ServerFixture fixture)
        : IClassFixture<ServerFixture>
    {
        private static string NewQueueName() => Guid.NewGuid().ToString("N");

        private CMqServer Server => fixture.Server;

        private int Depth(string queueName) => Server.GetQueue(queueName)?.QueueDepth ?? -1;

        private bool TryEnqueue(string queueName)
        {
            try
            {
                Server.Enqueue(queueName, null, null, "{}");
                return true;
            }
            catch (CMqQueueFullException)
            {
                return false;
            }
        }

        [Theory(DisplayName = "Depth limit: the limit is exact, even with many publishers at once.")]
        [InlineData(CMqPersistenceScheme.Ephemeral)]
        [InlineData(CMqPersistenceScheme.Persistent)]
        public void TestLimitIsExact(CMqPersistenceScheme persistenceScheme)
        {
            var queueName = NewQueueName();
            Server.CreateQueue(new CMqQueueConfiguration(queueName) { PersistenceScheme = persistenceScheme, MaxDepth = 100 });

            int accepted = 0;
            Parallel.For(0, 16, new ParallelOptions { MaxDegreeOfParallelism = 16 }, _ =>
            {
                for (int i = 0; i < 40; i++)
                {
                    if (TryEnqueue(queueName))
                    {
                        Interlocked.Increment(ref accepted);
                    }
                }
            });

            Assert.Equal(100, accepted);
            Assert.Equal(100, Depth(queueName));
            Assert.Equal(100UL, Server.GetQueue(queueName)?.ReceivedMessageCount);
            Assert.Equal(100, Server.GetQueue(queueName)?.MaxDepth);
        }

        [Fact(DisplayName = "Depth limit: a full queue accepts messages again once there is room.")]
        public async Task TestRoomFreesUp()
        {
            var queueName = NewQueueName();
            Server.CreateQueue(new CMqQueueConfiguration(queueName) { MaxDepth = 3 });

            Assert.True(TryEnqueue(queueName) && TryEnqueue(queueName) && TryEnqueue(queueName));
            var ex = Assert.Throws<CMqQueueFullException>(() => Server.Enqueue(queueName, null, null, "{}"));
            Assert.Equal(3, ex.MaxDepth);

            var leased = Assert.Single(await Server.ReceiveAsync(queueName, "worker", 1, TimeSpan.FromSeconds(5)));
            Assert.False(TryEnqueue(queueName)); //A leased message is still in the queue.

            Server.AcknowledgeLease(leased.LeaseId, CMqConsumeResult.Consumed());
            Assert.True(Wait.Until(() => Depth(queueName) == 2));
            Assert.True(TryEnqueue(queueName));
            Assert.False(TryEnqueue(queueName));
        }

        [Fact(DisplayName = "Depth limit: the limit can be raised, lowered below the depth, and removed.")]
        public void TestChangingTheLimit()
        {
            var queueName = NewQueueName();
            Server.CreateQueue(new CMqQueueConfiguration(queueName) { MaxDepth = 5 });
            for (int i = 0; i < 5; i++)
            {
                Assert.True(TryEnqueue(queueName));
            }

            Server.CreateQueue(new CMqQueueConfiguration(queueName) { MaxDepth = 7 });
            Assert.True(TryEnqueue(queueName) && TryEnqueue(queueName));
            Assert.False(TryEnqueue(queueName));

            //Lowering it does not remove anything, it only refuses new messages until the queue drains below it.
            Server.CreateQueue(new CMqQueueConfiguration(queueName) { MaxDepth = 2 });
            Assert.Equal(7, Depth(queueName));
            Assert.False(TryEnqueue(queueName));

            Server.CreateQueue(new CMqQueueConfiguration(queueName));
            Assert.Null(Server.GetQueue(queueName)?.MaxDepth);
            Assert.True(TryEnqueue(queueName));
            Assert.Equal(8, Depth(queueName));
        }

        [Fact(DisplayName = "Depth limit: dead-letter queues are not limited, so dead-lettering never fails.")]
        public async Task TestDeadLetterQueueIsNotLimited()
        {
            var queueName = NewQueueName();
            Server.CreateQueue(new CMqQueueConfiguration(queueName) { MaxDepth = 1, DeadLetterConfiguration = new CMqDeadLetterQueueConfiguration() });

            for (int i = 0; i < 3; i++)
            {
                Assert.True(TryEnqueue(queueName));
                var leased = Assert.Single(await Server.ReceiveAsync(queueName, "worker", 1, TimeSpan.FromSeconds(5)));
                Server.AcknowledgeLease(leased.LeaseId, CMqConsumeResult.DeadLetter());
                Assert.True(Wait.Until(() => Depth(queueName) == 0));
            }

            Assert.True(Wait.Until(() => Depth($"{queueName}.dlq") == 3));
            Assert.Null(Server.GetQueue($"{queueName}.dlq")?.MaxDepth);
        }

        [Fact(DisplayName = "Depth limit: moving dead letters back stops when the queue is full.")]
        public async Task TestRedriveStopsWhenFull()
        {
            var queueName = NewQueueName();
            Server.CreateQueue(new CMqQueueConfiguration(queueName) { DeadLetterConfiguration = new CMqDeadLetterQueueConfiguration() });

            for (int i = 0; i < 5; i++)
            {
                Assert.True(TryEnqueue(queueName));
                var leased = Assert.Single(await Server.ReceiveAsync(queueName, "worker", 1, TimeSpan.FromSeconds(5)));
                Server.AcknowledgeLease(leased.LeaseId, CMqConsumeResult.DeadLetter());
            }
            Assert.True(Wait.Until(() => Depth($"{queueName}.dlq") == 5 && Depth(queueName) == 0));

            Server.CreateQueue(new CMqQueueConfiguration(queueName) { MaxDepth = 2, DeadLetterConfiguration = new CMqDeadLetterQueueConfiguration() });

            Assert.Equal(2, Server.RedriveDeadLetters(queueName));
            Assert.Equal(2, Depth(queueName));
            Assert.Equal(3, Depth($"{queueName}.dlq"));
        }

        [Fact(DisplayName = "Depth limit: a .NET client gets an error when the queue is full.")]
        public void TestClientIsTold()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();
            client.CreateQueue(new CMqQueueConfiguration(queueName) { MaxDepth = 1 });

            client.Enqueue(queueName, new KeyValueMessage(1, "x"));
            var ex = Assert.ThrowsAny<Exception>(() => client.Enqueue(queueName, new KeyValueMessage(2, "x")));
            Assert.Contains("full", ex.Message);

            //Expedient publishing does not wait for a reply, so it cannot be told. The message is not stored.
            client.EnqueueExpedient(queueName, new KeyValueMessage(3, "x"));
            Thread.Sleep(200);
            Assert.Equal(1, Depth(queueName));
        }
    }
}
