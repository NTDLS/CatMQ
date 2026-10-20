using CatMQ.Tests.Messages;
using NTDLS.CatMQ.Client;
using NTDLS.CatMQ.Server;
using NTDLS.CatMQ.Shared;
using System.Collections.Concurrent;

namespace CatMQ.Tests.Unit
{
    /// <summary>
    /// Each test runs its own server (own port and data folder) so that it can be stopped and started again.
    /// </summary>
    public class ServerRestart : IDisposable
    {
        private static int _nextPort = 45760;

        private readonly int _port = Interlocked.Increment(ref _nextPort);
        private readonly string _path = Path.Join(Path.GetTempPath(), "CatMQ.Tests", Guid.NewGuid().ToString("N"));

        private CMqServer StartServer()
        {
            var server = new CMqServer(new CMqServerConfiguration { PersistencePath = _path });
            server.Start(_port);
            return server;
        }

        private CMqClient Connect()
        {
            var client = new CMqClient();
            client.Connect(Constants.HOST_NAME, _port);
            return client;
        }

        [Fact(DisplayName = "Restart: persistent queues and their waiting messages survive, ephemeral queues do not.")]
        public void TestPersistentMessagesSurviveRestart()
        {
            string persistentQueue = "persistent" + Guid.NewGuid().ToString("N");
            string ephemeralQueue = "ephemeral" + Guid.NewGuid().ToString("N");

            var server = StartServer();
            try
            {
                using (var client = Connect())
                {
                    client.CreateQueue(new CMqQueueConfiguration(persistentQueue)
                    {
                        PersistenceScheme = CMqPersistenceScheme.Persistent,
                        MaxDeliveryAttempts = 7
                    });
                    client.CreateQueue(new CMqQueueConfiguration(ephemeralQueue) { PersistenceScheme = CMqPersistenceScheme.Ephemeral });

                    for (int i = 0; i < 30; i++)
                    {
                        client.Enqueue(persistentQueue, new KeyValueMessage(i, $"Value:{i}"));
                        client.Enqueue(ephemeralQueue, new KeyValueMessage(i, $"Value:{i}"));
                    }
                }
            }
            finally
            {
                server.Stop();
            }

            server = StartServer();
            try
            {
                Assert.Null(server.GetQueue(ephemeralQueue));

                var queue = server.GetQueue(persistentQueue);
                Assert.NotNull(queue);
                Assert.True(queue.IsInitialized);
                Assert.Equal(7, queue.MaxDeliveryAttempts);
                Assert.Equal(30, queue.QueueDepth);

                var received = new ConcurrentDictionary<int, string>();
                using var client = Connect();
                client.Subscribe(persistentQueue, (_, m) =>
                {
                    var kv = (KeyValueMessage)m.Deserialize();
                    received[kv.Key] = kv.Value;
                    return CMqConsumeResult.Consumed();
                });

                Assert.True(Wait.Until(() => received.Count == 30, 20));
                Assert.All(received, kvp => Assert.Equal($"Value:{kvp.Key}", kvp.Value));
                Assert.True(Wait.Until(() => server.GetQueue(persistentQueue)?.QueueDepth == 0));
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact(DisplayName = "Restart: messages enqueued after a restart do not collide with the persisted ones.")]
        public void TestSerialNumbersContinueAfterRestart()
        {
            string queueName = Guid.NewGuid().ToString("N");

            var server = StartServer();
            try
            {
                using var client = Connect();
                client.CreateQueue(new CMqQueueConfiguration(queueName) { PersistenceScheme = CMqPersistenceScheme.Persistent });
                for (int i = 0; i < 10; i++)
                {
                    client.Enqueue(queueName, new KeyValueMessage(i, "before"));
                }
            }
            finally
            {
                server.Stop();
            }

            server = StartServer();
            try
            {
                using var client = Connect();
                for (int i = 10; i < 20; i++)
                {
                    client.Enqueue(queueName, new KeyValueMessage(i, "after"));
                }

                //If serial numbers restarted from zero, the new messages would overwrite the old ones in the database.
                Assert.Equal(20, server.GetQueue(queueName)?.QueueDepth);
                Assert.Equal(20, server.GetQueueMessages(queueName, 0, int.MaxValue)?.Count);

                var received = new ConcurrentDictionary<int, string>();
                client.Subscribe(queueName, (_, m) =>
                {
                    var kv = (KeyValueMessage)m.Deserialize();
                    received[kv.Key] = kv.Value;
                    return CMqConsumeResult.Consumed();
                });

                Assert.True(Wait.Until(() => received.Count == 20, 20));
                Assert.Equal(10, received.Values.Count(o => o == "before"));
                Assert.Equal(10, received.Values.Count(o => o == "after"));
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact(DisplayName = "Restart: persistent dead-letter queue and its messages survive.")]
        public void TestDeadLetterSurvivesRestart()
        {
            string queueName = Guid.NewGuid().ToString("N");

            var server = StartServer();
            try
            {
                using var client = Connect();
                client.CreateQueue(new CMqQueueConfiguration(queueName)
                {
                    PersistenceScheme = CMqPersistenceScheme.Persistent,
                    DeadLetterConfiguration = new() { PersistenceScheme = CMqPersistenceScheme.Persistent }
                });
                client.Subscribe(queueName, (_, _) => CMqConsumeResult.DeadLetter());

                for (int i = 0; i < 10; i++)
                {
                    client.Enqueue(queueName, new KeyValueMessage(i, "x"));
                }

                Assert.True(Wait.Until(() => server.GetQueue($"{queueName}.dlq")?.QueueDepth == 10));
            }
            finally
            {
                server.Stop();
            }

            server = StartServer();
            try
            {
                Assert.Equal(10, server.GetQueue($"{queueName}.dlq")?.QueueDepth);
                Assert.Equal(0, server.GetQueue(queueName)?.QueueDepth);
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact(DisplayName = "Restart: a deleted queue stays deleted.")]
        public void TestDeletedQueueStaysDeleted()
        {
            string queueName = Guid.NewGuid().ToString("N");

            var server = StartServer();
            try
            {
                using var client = Connect();
                client.CreateQueue(new CMqQueueConfiguration(queueName) { PersistenceScheme = CMqPersistenceScheme.Persistent });
                client.Enqueue(queueName, new KeyValueMessage(1, "x"));
                client.DeleteQueue(queueName);
            }
            finally
            {
                server.Stop();
            }

            server = StartServer();
            try
            {
                Assert.Null(server.GetQueue(queueName));
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact(DisplayName = "A persistent queue cannot be created when the server has no persistence path.")]
        public void TestPersistentQueueWithoutPathFails()
        {
            var server = new CMqServer(new CMqServerConfiguration { PersistencePath = null });
            server.Start(Interlocked.Increment(ref _nextPort));
            try
            {
                var queueName = Guid.NewGuid().ToString("N");

                Assert.ThrowsAny<Exception>(() => server.CreateQueue(new CMqQueueConfiguration(queueName) { PersistenceScheme = CMqPersistenceScheme.Persistent }));

                //The failed create must not leave a half-created queue behind.
                Assert.Null(server.GetQueue(queueName));
            }
            finally
            {
                server.Stop();
            }
        }

        [Fact(DisplayName = "Enqueue on a stopped server fails instead of silently succeeding.")]
        public void TestEnqueueOnStoppedServerFails()
        {
            var server = StartServer();
            var queueName = Guid.NewGuid().ToString("N");
            server.CreateQueue(new CMqQueueConfiguration(queueName));
            server.Stop();

            Assert.ThrowsAny<Exception>(() => server.Enqueue(queueName, null, typeof(KeyValueMessage).AssemblyQualifiedName!, "{}"));
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_path, true);
            }
            catch
            {
            }
            GC.SuppressFinalize(this);
        }
    }
}
