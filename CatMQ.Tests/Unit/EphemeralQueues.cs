using CatMQ.Tests.Messages;
using NTDLS.CatMQ.Client;
using NTDLS.CatMQ.Shared;
using System.Collections.Concurrent;

namespace CatMQ.Tests.Unit
{
    public class EphemeralQueues(ServerFixture fixture)
        : IClassFixture<ServerFixture>
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

            var subscribers = fixture.Server.GetSubscribers(queueName);
            Assert.NotNull(subscribers);
            Assert.Equal(1, subscribers?.Count);

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

                return CMqConsumeResult.Consumed();
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

        [Fact(DisplayName = "Consume dead-letter.")]
        public void TestConsumeDeadLetter()
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

            var subscribers = fixture.Server.GetSubscribers(queueName);
            Assert.NotNull(subscribers);
            Assert.Equal(1, subscribers?.Count);

            client.Subscribe($"{queueName}.dlq", OnDeadLetterMessageReceived);

            subscribers = fixture.Server.GetSubscribers($"{queueName}.dlq");
            Assert.NotNull(subscribers);
            Assert.Equal(1, subscribers?.Count);

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

                return CMqConsumeResult.DeadLetter();
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

                return CMqConsumeResult.Consumed();
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
            int enqueuedCount = 0;
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

            var subscribers = fixture.Server.GetSubscribers(queueName);
            Assert.NotNull(subscribers);
            Assert.Equal(1, subscribers?.Count);

            //Receive messages and dead letter them.
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

                return CMqConsumeResult.DeadLetter();
            }

            //Enqueue messages.
            for (int i = 0; i < 100; i++)
            {
                enqueuedCount++;
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

            //Make sure the dead-letter queue has the same number of messages as the original queue.
            var deadLetterMessages = fixture.Server.GetQueueMessages($"{queueName}.dlq", 0, int.MaxValue);
            Assert.NotNull(deadLetterMessages);
            Assert.Equal(enqueuedCount, deadLetterMessages.Count);

            client.Disconnect();
        }

        [Fact(DisplayName = "Consume, defer and redelivery.")]
        public void TestConsumeDeferAndRedelivery()
        {
            ConcurrentDictionary<int, string> messages = new();

            var client = ClientFactory.CreateAndConnect();

            string queueName = Guid.NewGuid().ToString("N");

            client.CreateQueue(new CMqQueueConfiguration(queueName)
            {
                MaxDeliveryAttempts = 100, //This must be greater than the deferred count.
                PersistenceScheme = CMqPersistenceScheme.Ephemeral,
                ConsumptionScheme = CMqConsumptionScheme.Delivered,
            });

            client.Subscribe(queueName, OnMessageReceived);

            var subscribers = fixture.Server.GetSubscribers(queueName);
            Assert.NotNull(subscribers);
            Assert.Equal(1, subscribers?.Count);

            CMqConsumeResult OnMessageReceived(CMqClient client, CMqReceivedMessage rawMessage)
            {
                if (rawMessage.DeferredCount < 10)
                {
                    return CMqConsumeResult.Defer(TimeSpan.FromMilliseconds(100));
                }

                //If the message has been deferred 10 times, we can consume it.

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

                return CMqConsumeResult.Consumed();
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
            while (!messages.IsEmpty && DateTime.UtcNow - startTime < TimeSpan.FromSeconds(30))
            {
                Thread.Sleep(10);
            }

            Assert.Empty(messages);

            client.Disconnect();
        }

        [Fact(DisplayName = "Enqueue and expire.")]
        public void TestEnqueueAndExpire()
        {
            int enqueuedCount = 0;

            TimeSpan maxMessageAge = TimeSpan.FromSeconds(2);

            var client = ClientFactory.CreateAndConnect();

            string queueName = Guid.NewGuid().ToString("N");

            client.CreateQueue(new CMqQueueConfiguration(queueName)
            {
                MaxMessageAge = maxMessageAge,
                PersistenceScheme = CMqPersistenceScheme.Ephemeral,
                ConsumptionScheme = CMqConsumptionScheme.Delivered,
            });

            //Enqueue messages.
            for (int i = 0; i < 100; i++)
            {
                client.Enqueue(queueName, new KeyValueMessage(i, $"Value:{i}"));
                enqueuedCount++;
            }

            Thread.Sleep((int)(maxMessageAge + TimeSpan.FromSeconds(1)).TotalMilliseconds);

            //Make sure the queue is empty.
            var messages = fixture.Server.GetQueueMessages(queueName, 0, int.MaxValue);
            Assert.NotNull(messages);
            Assert.Equal(0, messages.Count);

            //Make sure the expired message count is equal to the enqueued count.
            var queue = fixture.Server.GetQueues()?.FirstOrDefault(q => q.QueueName == queueName);
            Assert.NotNull(queue);
            Assert.Equal(enqueuedCount, (int?)queue?.ExpiredMessageCount);

            client.Disconnect();
        }
    }
}
