using CatMQ.Tests.Messages;
using NTDLS.CatMQ.Client;
using NTDLS.CatMQ.Server;
using NTDLS.CatMQ.Shared;
using System.Collections.Concurrent;

namespace CatMQ.Tests.Unit
{
    /// <summary>
    /// Every message carries a unique ID that does not change when it is redelivered, dead-lettered or restored after a restart,
    /// so that subscribers can use it to detect a message that they have already handled.
    /// </summary>
    public class MessageIds(ServerFixture fixture)
        : IClassFixture<ServerFixture>
    {
        private static string NewQueueName() => Guid.NewGuid().ToString("N");

        [Theory(DisplayName = "Each message gets a unique, non-empty ID.")]
        [InlineData(CMqPersistenceScheme.Ephemeral)]
        [InlineData(CMqPersistenceScheme.Persistent)]
        public void TestIdsAreUnique(CMqPersistenceScheme persistence)
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();
            client.CreateQueue(new CMqQueueConfiguration(queueName) { PersistenceScheme = persistence });

            var ids = new ConcurrentBag<Guid>();
            client.Subscribe(queueName, (_, m) => { ids.Add(m.MessageId); return CMqConsumeResult.Consumed(); });

            for (int i = 0; i < 50; i++)
            {
                client.Enqueue(queueName, new KeyValueMessage(i, "x"));
            }

            Assert.True(Wait.Until(() => ids.Count == 50));
            Assert.DoesNotContain(Guid.Empty, ids);
            Assert.Equal(50, ids.Distinct().Count());
        }

        [Theory(DisplayName = "A redelivered message keeps the same ID.")]
        [InlineData(CMqPersistenceScheme.Ephemeral)]
        [InlineData(CMqPersistenceScheme.Persistent)]
        public void TestIdIsStableAcrossRedelivery(CMqPersistenceScheme persistence)
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();
            client.CreateQueue(new CMqQueueConfiguration(queueName) { PersistenceScheme = persistence, MaxDeliveryAttempts = 0 });

            var ids = new ConcurrentQueue<Guid>();
            client.Subscribe(queueName, (_, m) =>
            {
                ids.Enqueue(m.MessageId);
                return ids.Count < 3 ? CMqConsumeResult.NotConsumed() : CMqConsumeResult.Consumed();
            });

            client.Enqueue(queueName, new KeyValueMessage(1, "x"));

            Assert.True(Wait.Until(() => ids.Count == 3));
            Assert.Single(ids.Distinct());
            Assert.NotEqual(Guid.Empty, ids.First());
        }

        [Fact(DisplayName = "The ID shown in the queue listing is the ID that is delivered.")]
        public void TestListingMatchesDelivery()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();
            client.CreateQueue(queueName);

            client.Enqueue(queueName, new KeyValueMessage(1, "x"));

            var listed = fixture.Server.GetQueueMessages(queueName, 0, 10)?.Messages.Single();
            Assert.NotNull(listed);
            Assert.NotEqual(Guid.Empty, listed.MessageId);
            Assert.Equal(listed.MessageId, fixture.Server.GetQueueMessage(queueName, listed.SerialNumber)?.MessageId);

            Guid? delivered = null;
            client.Subscribe(queueName, (_, m) => { delivered = m.MessageId; return CMqConsumeResult.Consumed(); });

            Assert.True(Wait.Until(() => delivered != null));
            Assert.Equal(listed.MessageId, delivered);
        }

        [Fact(DisplayName = "A dead-lettered message keeps its ID.")]
        public void TestIdSurvivesDeadLettering()
        {
            using var client = ClientFactory.CreateAndConnect();
            var queueName = NewQueueName();
            client.CreateQueue(new CMqQueueConfiguration(queueName)
            {
                DeadLetterConfiguration = new() { PersistenceScheme = CMqPersistenceScheme.Ephemeral }
            });

            Guid? original = null;
            client.Subscribe(queueName, (_, m) => { original = m.MessageId; return CMqConsumeResult.DeadLetter(); });

            Guid? deadLettered = null;
            client.Subscribe($"{queueName}.dlq", (_, m) => { deadLettered = m.MessageId; return CMqConsumeResult.Consumed(); });

            client.Enqueue(queueName, new KeyValueMessage(1, "x"));

            Assert.True(Wait.Until(() => original != null && deadLettered != null));
            Assert.NotEqual(Guid.Empty, original);
            Assert.Equal(original, deadLettered);
        }

        [Fact(DisplayName = "Persisted messages keep their IDs across a server restart.")]
        public void TestIdSurvivesRestart()
        {
            int port = 45790;
            string path = Path.Join(Path.GetTempPath(), "CatMQ.Tests", Guid.NewGuid().ToString("N"));
            string queueName = NewQueueName();

            CMqServer StartServer()
            {
                var server = new CMqServer(new CMqServerConfiguration { PersistencePath = path });
                server.Start(port);
                return server;
            }

            CMqClient Connect()
            {
                var client = new CMqClient();
                client.Connect(Constants.HOST_NAME, port);
                return client;
            }

            try
            {
                HashSet<Guid> before;

                var server = StartServer();
                try
                {
                    using var client = Connect();
                    client.CreateQueue(new CMqQueueConfiguration(queueName) { PersistenceScheme = CMqPersistenceScheme.Persistent });
                    for (int i = 0; i < 20; i++)
                    {
                        client.Enqueue(queueName, new KeyValueMessage(i, "x"));
                    }

                    before = server.GetQueueMessages(queueName, 0, int.MaxValue)!.Messages.Select(o => o.MessageId).ToHashSet();
                    Assert.Equal(20, before.Count);
                    Assert.DoesNotContain(Guid.Empty, before);
                }
                finally
                {
                    server.Stop();
                }

                server = StartServer();
                try
                {
                    using var client = Connect();
                    var after = new ConcurrentBag<Guid>();
                    client.Subscribe(queueName, (_, m) => { after.Add(m.MessageId); return CMqConsumeResult.Consumed(); });

                    Assert.True(Wait.Until(() => after.Count == 20, 20));
                    Assert.True(before.SetEquals(after));
                }
                finally
                {
                    server.Stop();
                }
            }
            finally
            {
                try { Directory.Delete(path, true); } catch { }
            }
        }
    }
}
