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

            using var client = ClientFactory.CreateAndConnect();

            string queueName = Guid.NewGuid().ToString("N");

            client.CreateQueue(new CMqQueueConfiguration(queueName)
            {
                PersistenceScheme = CMqPersistenceScheme.Ephemeral,
                ConsumptionScheme = CMqConsumptionScheme.AllSubscribersSatisfied,
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
        }

        [Fact(DisplayName = "Consume dead-letter.")]
        public void TestConsumeDeadLetter()
        {
            ConcurrentDictionary<int, string> messages = new();

            using var client = ClientFactory.CreateAndConnect();

            string queueName = Guid.NewGuid().ToString("N");

            client.CreateQueue(new CMqQueueConfiguration(queueName)
            {
                PersistenceScheme = CMqPersistenceScheme.Ephemeral,
                ConsumptionScheme = CMqConsumptionScheme.AllSubscribersSatisfied,
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
            while (!messages.IsEmpty && DateTime.UtcNow - startTime < TimeSpan.FromSeconds(30))
            {
                Thread.Sleep(100);
            }

            Assert.Empty(messages);
        }

        [Fact(DisplayName = "Dead-lettering.")]
        public void TestDeadLettering()
        {
            int enqueuedCount = 0;
            ConcurrentDictionary<int, string> messages = new();

            using var client = ClientFactory.CreateAndConnect();

            string queueName = Guid.NewGuid().ToString("N");

            client.CreateQueue(new CMqQueueConfiguration(queueName)
            {
                PersistenceScheme = CMqPersistenceScheme.Ephemeral,
                ConsumptionScheme = CMqConsumptionScheme.AllSubscribersSatisfied,
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
            while (!messages.IsEmpty && DateTime.UtcNow - startTime < TimeSpan.FromSeconds(30))
            {
                Thread.Sleep(100);
            }

            Assert.Empty(messages);

            //Make sure the dead-letter queue has the same number of messages as the original queue.
            var deadLetterMessages = fixture.Server.GetQueueMessages($"{queueName}.dlq", 0, int.MaxValue);
            Assert.NotNull(deadLetterMessages);
            Assert.Equal(enqueuedCount, deadLetterMessages.Count);
        }

        [Fact(DisplayName = "Consume, defer and redelivery.")]
        public void TestConsumeDeferAndRedelivery()
        {
            ConcurrentDictionary<int, string> messages = new();

            using var client = ClientFactory.CreateAndConnect();

            string queueName = Guid.NewGuid().ToString("N");

            client.CreateQueue(new CMqQueueConfiguration(queueName)
            {
                MaxDeliveryAttempts = 100, //This must be greater than the deferred count.
                PersistenceScheme = CMqPersistenceScheme.Ephemeral,
                ConsumptionScheme = CMqConsumptionScheme.AllSubscribersSatisfied,
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
                Thread.Sleep(100);
            }

            Assert.Empty(messages);
        }

        [Fact(DisplayName = "Enqueue and expire.")]
        public void TestEnqueueAndExpire()
        {
            int enqueuedCount = 0;
            var maxMessageAge = TimeSpan.FromSeconds(2);

            using var client = ClientFactory.CreateAndConnect();

            string queueName = Guid.NewGuid().ToString("N");

            client.CreateQueue(new CMqQueueConfiguration(queueName)
            {
                MaxMessageAge = maxMessageAge,
                PersistenceScheme = CMqPersistenceScheme.Ephemeral,
                ConsumptionScheme = CMqConsumptionScheme.AllSubscribersSatisfied,
            });

            //Enqueue messages.
            for (int i = 0; i < 100; i++)
            {
                client.Enqueue(queueName, new KeyValueMessage(i, $"Value:{i}"));
                enqueuedCount++;
            }

            //Give the messages time to expire.
            Thread.Sleep((int)(maxMessageAge + TimeSpan.FromSeconds(1)).TotalMilliseconds);

            //Make sure the queue is empty.
            var messages = fixture.Server.GetQueueMessages(queueName, 0, int.MaxValue);
            Assert.NotNull(messages);
            Assert.Equal(0, messages.Count);

            //Make sure the expired message count is equal to the enqueued count.
            var queue = fixture.Server.GetQueue(queueName);
            Assert.NotNull(queue);
            Assert.Equal(enqueuedCount, (int?)queue?.ExpiredMessageCount);
        }


        [Fact(DisplayName = "Enqueue before subscribing.")]
        public void TestEnqueueBeforeSubscribing()
        {
            var messages = new ConcurrentDictionary<int, string>();
            using var client = ClientFactory.CreateAndConnect();
            string queueName = Guid.NewGuid().ToString("N");

            client.CreateQueue(new CMqQueueConfiguration(queueName)
            {
                PersistenceScheme = CMqPersistenceScheme.Ephemeral,
                ConsumptionScheme = CMqConsumptionScheme.AllSubscribersSatisfied,
            });

            // Enqueue before subscribing
            for (int i = 0; i < 10; i++)
            {
                messages.TryAdd(i, $"Value:{i}");
                client.Enqueue(queueName, new KeyValueMessage(i, $"Value:{i}"));
            }

            client.Subscribe(queueName, (c, msg) =>
            {
                var kv = msg.Deserialize() as KeyValueMessage;
                Assert.NotNull(kv);
                messages.TryRemove(kv.Key, out _);
                return CMqConsumeResult.Consumed();
            });

            var startTime = DateTime.UtcNow;
            while (!messages.IsEmpty && DateTime.UtcNow - startTime < TimeSpan.FromSeconds(30))
            {
                Thread.Sleep(100);
            }

            Assert.Empty(messages);
        }

        [Fact(DisplayName = "Round-robin delivery to multiple subscribers.")]
        public void TestMultipleSubscribersRoundRobin()
        {
            var messageCount = 100;
            var receivedByClient1 = 0;
            var receivedByClient2 = 0;

            using var client1 = ClientFactory.CreateAndConnect();
            using var client2 = ClientFactory.CreateAndConnect();
            var publisher = ClientFactory.CreateAndConnect();
            string queueName = Guid.NewGuid().ToString("N");

            publisher.CreateQueue(new CMqQueueConfiguration(queueName)
            {
                PersistenceScheme = CMqPersistenceScheme.Ephemeral,
                ConsumptionScheme = CMqConsumptionScheme.FirstConsumedSubscriber,
                DeliveryScheme = CMqDeliveryScheme.Balanced
            });

            client1.Subscribe(queueName, (_, _) => { Interlocked.Increment(ref receivedByClient1); return CMqConsumeResult.Consumed(); });
            client2.Subscribe(queueName, (_, _) => { Interlocked.Increment(ref receivedByClient2); return CMqConsumeResult.Consumed(); });

            for (int i = 0; i < messageCount; i++)
            {
                publisher.Enqueue(queueName, new KeyValueMessage(i, $"Value:{i}"));
            }

            Thread.Sleep(2000); // Allow time for processing

            Assert.True(receivedByClient1 > 0);
            Assert.True(receivedByClient2 > 0);
            Assert.Equal(receivedByClient1, receivedByClient2);
            Assert.Equal(messageCount, receivedByClient1 + receivedByClient2);
        }

        [Fact(DisplayName = "Message redelivered after subscriber disconnect mid-delivery.")]
        public void TestRedeliveryAfterClientDisconnect()
        {
            var messageConsumed = false;
            var client = ClientFactory.CreateAndConnect();
            string queueName = Guid.NewGuid().ToString("N");

            client.CreateQueue(new CMqQueueConfiguration(queueName)
            {
                MaxDeliveryAttempts = 0,
                PersistenceScheme = CMqPersistenceScheme.Ephemeral,
                ConsumptionScheme = CMqConsumptionScheme.FirstConsumedSubscriber,
            });

            var tempClient = ClientFactory.CreateAndConnect();
            tempClient.Subscribe(queueName, (OnMessageReceived)((_, _) => { tempClient.Disconnect(); return CMqConsumeResult.NotInterested(); }));

            client.Enqueue(queueName, new KeyValueMessage(1, "Hello Delivery"));

            while (tempClient.IsConnected)
            {
                Thread.Sleep(100); // Wait for tempClient to disconnect
            }

            client.Subscribe(queueName, (_, msg) =>
            {
                messageConsumed = true;
                return CMqConsumeResult.Consumed();
            });

            var start = DateTime.UtcNow;
            while (!messageConsumed && DateTime.UtcNow - start < TimeSpan.FromSeconds(5))
            {
                Thread.Sleep(50);
            }

            Assert.True(messageConsumed);
        }

        [Fact(DisplayName = "Message redelivered after subscriber throws exception.")]
        public void TestRedeliveryAfterException()
        {
            var messageConsumed = false;
            var client = ClientFactory.CreateAndConnect();
            string queueName = Guid.NewGuid().ToString("N");

            client.CreateQueue(new CMqQueueConfiguration(queueName)
            {
                MaxDeliveryAttempts = 0,
                PersistenceScheme = CMqPersistenceScheme.Ephemeral,
                ConsumptionScheme = CMqConsumptionScheme.FirstConsumedSubscriber,
            });

            int receivedCount = 0;

            var tempClient = ClientFactory.CreateAndConnect();
            tempClient.Subscribe(queueName, (_, _) =>
            {
                Interlocked.Increment(ref receivedCount);
                throw new Exception("Some error");
            });

            client.Enqueue(queueName, new KeyValueMessage(1, "Hello Delivery"));

            var start = DateTime.UtcNow;
            while (receivedCount < 10 && DateTime.UtcNow - start < TimeSpan.FromSeconds(5))
            {
                Thread.Sleep(10);
            }

            client.Subscribe(queueName, (_, msg) =>
            {
                messageConsumed = true;
                return CMqConsumeResult.Consumed();
            });

            start = DateTime.UtcNow;
            while (!messageConsumed && DateTime.UtcNow - start < TimeSpan.FromSeconds(5))
            {
                Thread.Sleep(50);
            }

            Assert.True(messageConsumed);
        }


        [Fact(DisplayName = "MaxDeliveryAttempts prevents redelivery after limit reached.")]
        public void TestMaxDeliveryAttempts()
        {
            var deliveryCount = 0;
            var client = ClientFactory.CreateAndConnect();
            string queueName = Guid.NewGuid().ToString("N");

            client.CreateQueue(new CMqQueueConfiguration(queueName)
            {
                MaxDeliveryAttempts = 3, // Retry at most 3 times
                PersistenceScheme = CMqPersistenceScheme.Ephemeral,
                ConsumptionScheme = CMqConsumptionScheme.FirstConsumedSubscriber
            });

            client.Subscribe(queueName, (OnMessageReceived)((_, _) =>
            {
                Interlocked.Increment(ref deliveryCount);
                return CMqConsumeResult.NotConsumed();
            }));

            client.Enqueue(queueName, new KeyValueMessage(1, "Hello Retry Limit"));

            var start = DateTime.UtcNow;
            while (deliveryCount < 4 && DateTime.UtcNow - start < TimeSpan.FromSeconds(5))
            {
                Thread.Sleep(10);
            }

            // Should be delivered at most 3 times (original + 2 retries)
            Assert.Equal(3, deliveryCount);
        }

        [Fact(DisplayName = "MaxDeliveryAttempts with dead-letter.")]
        public void TestMaxDeliveryAttemptsWithDeadLetter()
        {
            var deliveryCount = 0;
            var client = ClientFactory.CreateAndConnect();
            string queueName = Guid.NewGuid().ToString("N");

            client.CreateQueue(new CMqQueueConfiguration(queueName)
            {
                MaxDeliveryAttempts = 3,
                PersistenceScheme = CMqPersistenceScheme.Ephemeral,
                ConsumptionScheme = CMqConsumptionScheme.FirstConsumedSubscriber,
                DeadLetterConfiguration = new()
                {
                    PersistenceScheme = CMqPersistenceScheme.Ephemeral
                }
            });

            client.Subscribe(queueName, (OnMessageReceived)((_, _) =>
            {
                Interlocked.Increment(ref deliveryCount);
                return CMqConsumeResult.NotConsumed();
            }));

            client.Enqueue(queueName, new KeyValueMessage(1, "Hello Retry Limit 1"));
            client.Enqueue(queueName, new KeyValueMessage(2, "Hello Retry Limit 2"));

            var start = DateTime.UtcNow;
            while (deliveryCount < 6 && DateTime.UtcNow - start < TimeSpan.FromSeconds(5))
            {
                Thread.Sleep(10);
            }

            // Should be delivered at most 3 times (original + 2 retries)
            Assert.Equal(6, deliveryCount);

            int deadLetterCount = 0;
            client.Subscribe($"{queueName}.dlq", (_, msg) =>
            {
                var kv = msg.Deserialize() as KeyValueMessage;
                Assert.NotNull(kv);
                Assert.InRange(1, 1, 2);
                Interlocked.Increment(ref deadLetterCount);
                return CMqConsumeResult.Consumed();
            });

            start = DateTime.UtcNow;
            while (deadLetterCount < 2 && DateTime.UtcNow - start < TimeSpan.FromSeconds(5))
            {
                Thread.Sleep(10);
            }

            Assert.Equal(2, deadLetterCount);
        }
    }
}
