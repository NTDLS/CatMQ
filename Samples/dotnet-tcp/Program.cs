using NTDLS.CatMQ.Client;
using NTDLS.CatMQ.Shared;
using System.Text;
using System.Text.Json.Serialization;

namespace Sample.DotNetTcp
{
    /// <summary>
    /// Publishes and consumes the sample messages with the .NET client, over TCP.
    ///
    ///   dotnet run -- publish [count]
    ///   dotnet run -- consume [--until-idle]
    /// </summary>
    internal class Program
    {
        private const string QueueName = "samples.orders";

        /// <summary>
        /// The JSON names are camelCase, which is what the publishers in other languages send.
        /// </summary>
        internal class OrderCreated : ICMqMessage
        {
            [JsonPropertyName("orderId")] public int OrderId { get; set; }
            [JsonPropertyName("customer")] public string? Customer { get; set; }
            [JsonPropertyName("total")] public decimal Total { get; set; }
        }

        static async Task Main(string[] args)
        {
            var host = Environment.GetEnvironmentVariable("CATMQ_HOST") ?? "127.0.0.1";
            var port = int.Parse(Environment.GetEnvironmentVariable("CATMQ_PORT") ?? CMqDefaults.DEFAULT_LISTEN_PORT.ToString());
            var mode = args.FirstOrDefault() ?? "publish";

            //Messages from other languages say "orders.created", not a .NET type name. Registering the name maps it to the type
            //  both ways: typed messages are sent with this name, and messages with this name deserialize to this type.
            CMqSerialization.RegisterMessageType<OrderCreated>("orders.created");

            var client = new CMqClient(new CMqClientConfiguration
            {
                //Consumers that share a name compete for messages, like several workers of one service.
                ConsumerName = "dotnet-tcp-worker"
            });
            client.Connect(host, port);

            //Creates the queue, or applies these settings to it. Every sample uses the same ones.
            client.CreateQueue(new CMqQueueConfiguration(QueueName)
            {
                PersistenceScheme = CMqPersistenceScheme.Persistent,
                ConsumptionScheme = CMqConsumptionScheme.FirstConsumedSubscriber,
                MaxOutstandingDeliveries = 10,
                DeadLetterConfiguration = new CMqDeadLetterQueueConfiguration { PersistenceScheme = CMqPersistenceScheme.Persistent }
            });

            try
            {
                if (mode == "publish")
                {
                    await PublishAsync(client, int.Parse(args.ElementAtOrDefault(1) ?? "5"));
                }
                else if (mode == "consume")
                {
                    await ConsumeAsync(client, untilIdle: args.Contains("--until-idle"));
                }
                else
                {
                    Console.WriteLine("Usage: publish [count] | consume [--until-idle]");
                }
            }
            finally
            {
                client.Disconnect();
            }
        }

        private static async Task PublishAsync(CMqClient client, int count)
        {
            var headers = new Dictionary<string, string> { ["source"] = "dotnet-tcp" };

            //A typed message. It is sent as JSON with the registered message type name.
            for (int i = 1; i <= count; i++)
            {
                await client.EnqueueAsync(QueueName, new OrderCreated { OrderId = i, Customer = "Ada", Total = 10.5m * i },
                    new CMqEnqueueOptions { Headers = headers });
            }

            //The same kind of message as a message type and a JSON body, as any language would send it. This one has no
            //  customer, so the consumers dead-letter it.
            await client.EnqueueAsync(QueueName, "orders.created", """{"orderId":999,"total":1}""", new CMqEnqueueOptions { Headers = headers });

            //Text, with a content type.
            await client.EnqueueAsync(QueueName, "orders.note", "Order 1 is a gift; please wrap it.",
                new CMqEnqueueOptions { ContentType = "text/plain", Headers = headers });

            //Binary, base64 encoded. Consumers in other languages receive it as bytes.
            await client.EnqueueAsync(QueueName, "orders.receipt", Convert.ToBase64String("%PDF-1.7 receipt"u8.ToArray()),
                new CMqEnqueueOptions { ContentType = "application/octet-stream", BodyIsBase64 = true, Headers = headers });

            //Delivered in 5 seconds, not now.
            await client.EnqueueAsync(QueueName, "orders.note", "Sent with a 5 second delay.",
                new CMqEnqueueOptions { ContentType = "text/plain", Headers = headers, DeferDeliveryDuration = TimeSpan.FromSeconds(5) });

            Console.WriteLine($"Published {count + 4} messages to {QueueName}.");
        }

        private static async Task ConsumeAsync(CMqClient client, bool untilIdle)
        {
            var lastMessageAt = DateTime.UtcNow;

            //An asynchronous handler does not hold a thread while it waits, so use one for work that does I/O.
            await client.SubscribeAsync(QueueName, async (_, message) =>
            {
                lastMessageAt = DateTime.UtcNow;
                return await HandleAsync(message);
            });

            Console.WriteLine(untilIdle ? $"Consuming {QueueName} until it is idle..." : $"Consuming {QueueName}. Press Ctrl+C to stop.");

            using var stop = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
            try
            {
                while (untilIdle == false || DateTime.UtcNow - lastMessageAt < TimeSpan.FromSeconds(7))
                {
                    await Task.Delay(250, stop.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static async Task<CMqConsumeResult> HandleAsync(CMqReceivedMessage message)
        {
            var source = message.Headers?.GetValueOrDefault("source") ?? "unknown";

            switch (message.MessageType)
            {
                case "orders.created":
                    var order = message.Deserialize<OrderCreated>();
                    if (string.IsNullOrEmpty(order?.Customer))
                    {
                        Console.WriteLine($"Order {order?.OrderId} from {source} has no customer: dead-lettering it.");
                        return CMqConsumeResult.DeadLetter();
                    }
                    await Task.Delay(10); //Stands in for real work, such as a database call.
                    Console.WriteLine($"Order {order.OrderId} for {order.Customer}, {order.Total:0.00}, from {source} (attempt {message.DeliveryAttempt}).");
                    return CMqConsumeResult.Consumed();

                case "orders.note":
                    Console.WriteLine($"Note from {source}: {message.Body}");
                    return CMqConsumeResult.Consumed();

                case "orders.receipt":
                    var bytes = message.GetBodyBytes();
                    Console.WriteLine($"Receipt from {source}: {bytes.Length} bytes of {message.ContentType}, starting \"{Encoding.ASCII.GetString(bytes, 0, Math.Min(8, bytes.Length))}\".");
                    return CMqConsumeResult.Consumed();

                default:
                    //Not for this consumer. Another one may want it.
                    return CMqConsumeResult.NotInterested();
            }
        }
    }
}
