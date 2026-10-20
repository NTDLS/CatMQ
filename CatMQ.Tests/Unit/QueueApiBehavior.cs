using CatMQ.Tests.Messages;
using NTDLS.CatMQ.Shared;

namespace CatMQ.Tests.Unit
{
    public class QueueApiBehavior(ServerFixture fixture)
        : IClassFixture<ServerFixture>
    {
        [Fact(DisplayName = "Failed subscribe can be retried once the queue exists.")]
        public void TestFailedSubscribeCanBeRetried()
        {
            using var client = ClientFactory.CreateAndConnect();
            string queueName = Guid.NewGuid().ToString("N");

            Assert.ThrowsAny<Exception>(() => client.Subscribe(queueName, (c, m) => CMqConsumeResult.Consumed()));

            client.CreateQueue(queueName);

            //Would throw "already subscribed" if the failed attempt had left a local subscription behind.
            client.Subscribe(queueName, (c, m) => CMqConsumeResult.Consumed());
        }

        [Fact(DisplayName = "Enqueue to a missing queue fails.")]
        public void TestEnqueueToMissingQueueFails()
        {
            using var client = ClientFactory.CreateAndConnect();
            Assert.ThrowsAny<Exception>(() => client.Enqueue(Guid.NewGuid().ToString("N"), new KeyValueMessage(1, "x")));
        }

        [Fact(DisplayName = "Purge empties the queue and depth does not go negative.")]
        public void TestPurgeKeepsDepthConsistent()
        {
            using var client = ClientFactory.CreateAndConnect();
            string queueName = Guid.NewGuid().ToString("N");
            client.CreateQueue(queueName);

            for (int i = 0; i < 50; i++)
            {
                client.Enqueue(queueName, new KeyValueMessage(i, "x"));
            }

            client.PurgeQueue(queueName);

            var queue = fixture.Server.GetQueue(queueName);
            Assert.NotNull(queue);
            Assert.True(queue.QueueDepth >= 0);
            Assert.Equal(0, queue.QueueDepth);
        }
    }
}
