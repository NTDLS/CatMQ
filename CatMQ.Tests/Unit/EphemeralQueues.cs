using CatMQ.Tests.Messages;
using NTDLS.CatMQ.Client;
using NTDLS.CatMQ.Shared;
using System.Collections.Concurrent;

namespace CatMQ.Tests.Unit
{
    public class EphemeralQueues(ServerFixture fixture) : IClassFixture<ServerFixture>
    {
        [Fact(DisplayName = "Simple enqueue and consume.")]
        public void TestSimpleEnqueueAndConsume()
        {
            ConcurrentDictionary<int, string> messages = new();

            var client = ClientFactory.CreateAndConnect();

            string queueName = Guid.NewGuid().ToString("N");

            client.CreateQueue(new CMqQueueConfiguration(queueName)
            {
                PersistenceScheme = CMqPersistenceScheme.Ephemeral,
                ConsumptionScheme = CMqConsumptionScheme.Delivered,
            });

            client.Subscribe(queueName, OnMessageReceived);

            CMqConsumeResult OnMessageReceived(CMqClient client, CMqReceivedMessage rawMessage)
            {
                var message = rawMessage.Deserialize();
                if (message is KeyValueMessage keyValue)
                {
                    if (!messages.TryRemove(keyValue.Key, out var originalMessage))
                    {
                        Assert.Fail($"Failed to remove message with key {keyValue.Key} from the dictionary.");
                    }

                    Assert.Equal(originalMessage, keyValue.Value);
                }
                else
                {
                    Assert.Fail($"Unexpected message type: {message.GetType()}");
                }

                return CMqConsumeResult.FromDisposition(CMqConsumptionDisposition.Consumed);
            }

            //Enqueue messages.
            for (int i = 0; i < 100; i++)
            {
                var message = $"Value:{i}";
                messages.TryAdd(i, message);
                client.Enqueue(queueName, new KeyValueMessage(i, message));
            }

            //Wait for messages to be consumed.
            var startTime = DateTime.UtcNow;
            while (!messages.IsEmpty && DateTime.UtcNow - startTime < TimeSpan.FromSeconds(5))
            {
                Thread.Sleep(10);
            }

            Assert.Empty(messages);

            client.Disconnect();
        }

        [Fact(DisplayName = "Dead-lettering.")]
        public void TestDeadLettering()
        {
            ConcurrentDictionary<int, string> messages = new();

            var client = ClientFactory.CreateAndConnect();

            string queueName = Guid.NewGuid().ToString("N");

            client.CreateQueue(new CMqQueueConfiguration(queueName)
            {
                PersistenceScheme = CMqPersistenceScheme.Ephemeral,
                ConsumptionScheme = CMqConsumptionScheme.Delivered,
                DeadLetterConfiguration = new()
                {
                    PersistenceScheme = CMqPersistenceScheme.Ephemeral
                }
            });

            client.Subscribe(queueName, OnMessageReceived);
            client.Subscribe($"{queueName}.dlq", OnDeadLetterMessageReceived);

            //Receive messages and dead letter them.
            CMqConsumeResult OnMessageReceived(CMqClient client, CMqReceivedMessage rawMessage)
            {
                var message = rawMessage.Deserialize();
                if (message is KeyValueMessage keyValue)
                {
                    if (messages.TryGetValue(keyValue.Key, out var originalMessage))
                    {
                        Assert.Equal(originalMessage, keyValue.Value);
                    }
                    else
                    {
                        Assert.Fail($"Failed to get message with key {keyValue.Key} from the dictionary.");
                    }
                }
                else
                {
                    Assert.Fail($"Unexpected message type: {message.GetType()}");
                }

                return CMqConsumeResult.FromDisposition(CMqConsumptionDisposition.DeadLetter);
            }

            //Receive dead-lettered messages.
            CMqConsumeResult OnDeadLetterMessageReceived(CMqClient client, CMqReceivedMessage rawMessage)
            {
                var message = rawMessage.Deserialize();
                if (message is KeyValueMessage keyValue)
                {
                    if (!messages.TryRemove(keyValue.Key, out var originalMessage))
                    {
                        Assert.Fail($"Failed to remove message with key {keyValue.Key} from the dictionary.");
                    }
                    Assert.Equal(originalMessage, keyValue.Value);
                }
                else
                {
                    Assert.Fail($"Unexpected message type: {message.GetType()}");
                }

                return CMqConsumeResult.FromDisposition(CMqConsumptionDisposition.Consumed);
            }

            //Enqueue messages.
            for (int i = 0; i < 100; i++)
            {
                var message = $"Value:{i}";
                messages.TryAdd(i, message);
                client.Enqueue(queueName, new KeyValueMessage(i, message));
            }

            //Wait for messages to be consumed.
            var startTime = DateTime.UtcNow;
            while (!messages.IsEmpty && DateTime.UtcNow - startTime < TimeSpan.FromSeconds(5))
            {
                Thread.Sleep(10);
            }

            Assert.Empty(messages);

            client.Disconnect();
        }
    }
}
