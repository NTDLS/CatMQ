using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Sample.DotNetHttp
{
    /// <summary>
    /// Publishes and consumes the sample messages through the HTTP API, with HttpClient and no CatMQ package.
    ///
    ///   dotnet run -- publish [count]
    ///   dotnet run -- consume [--until-idle]
    /// </summary>
    internal class Program
    {
        private const string QueueName = "samples.orders";
        private const string Consumer = "dotnet-http-worker";

        //The API's JSON is camelCase, which JsonSerializerDefaults.Web reads and writes.
        private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

        private static HttpClient _http = new();

        internal record OrderCreated(int OrderId, string? Customer, decimal Total);

        static async Task Main(string[] args)
        {
            var url = (Environment.GetEnvironmentVariable("CATMQ_URL") ?? "http://127.0.0.1:45783").TrimEnd('/');
            var mode = args.FirstOrDefault() ?? "publish";

            _http = new HttpClient
            {
                BaseAddress = new Uri(url + "/api/v1/"),
                Timeout = TimeSpan.FromSeconds(90) //Longer than the longest receive wait.
            };
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Environment.GetEnvironmentVariable("CATMQ_API_KEY"));

            //Creates the queue, or applies these settings to it. Every sample uses the same ones.
            await Send(HttpMethod.Put, $"queues/{QueueName}", new
            {
                persistence = "persistent",
                consumptionScheme = "firstConsumer",
                maxOutstandingDeliveries = 10,
                deadLetter = new { persistence = "persistent" }
            });

            if (mode == "publish")
            {
                await PublishAsync(int.Parse(args.ElementAtOrDefault(1) ?? "5"));
            }
            else if (mode == "consume")
            {
                await ConsumeAsync(untilIdle: args.Contains("--until-idle"));
            }
            else
            {
                Console.WriteLine("Usage: publish [count] | consume [--until-idle]");
            }
        }

        private static async Task PublishAsync(int count)
        {
            var headers = new Dictionary<string, string> { ["source"] = "dotnet-http" };

            //One message per request. A JSON body is sent as ordinary JSON.
            await Send(HttpMethod.Post, $"queues/{QueueName}/messages", new
            {
                messageType = "orders.created",
                body = new { orderId = 1, customer = "Ada", total = 10.5 },
                headers
            });

            //Up to 1,000 per request. The idempotency keys make the request safe to retry: a key seen in the last 24 hours
            //  is reported as a duplicate, not stored again.
            var batch = Enumerable.Range(2, Math.Max(0, count - 1))
                .Select(i => (object)new
                {
                    messageType = "orders.created",
                    body = new { orderId = i, customer = "Ada", total = 10.5 * i },
                    headers,
                    idempotencyKey = $"dotnet-http-order-{i}"
                })
                .Append(new { messageType = "orders.created", body = new { orderId = 999, total = 1 }, headers }) //No customer: dead-lettered.
                .Append(new { messageType = "orders.note", contentType = "text/plain", body = "Order 1 is a gift; please wrap it.", headers })
                .Append(new { messageType = "orders.receipt", contentType = "application/octet-stream", bodyBase64 = Convert.ToBase64String("%PDF-1.7 receipt"u8.ToArray()), headers })
                .Append(new { messageType = "orders.note", contentType = "text/plain", body = "Sent with a 5 second delay.", headers, deferSeconds = 5 })
                .ToArray();

            var results = await Send(HttpMethod.Post, $"queues/{QueueName}/messages", new { messages = batch });
            var statuses = results.GetProperty("results").EnumerateArray().Select(o => o.GetProperty("status").GetString()).ToList();

            Console.WriteLine($"Published {1 + statuses.Count(o => o == "accepted")} messages to {QueueName}"
                + (statuses.Contains("duplicate") ? $" ({statuses.Count(o => o == "duplicate")} were duplicates of earlier runs)." : "."));
        }

        private static async Task ConsumeAsync(bool untilIdle)
        {
            using var stop = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
            Console.WriteLine(untilIdle ? $"Consuming {QueueName} until it is idle..." : $"Consuming {QueueName}. Press Ctrl+C to stop.");

            while (stop.IsCancellationRequested == false)
            {
                //Waits up to waitSeconds for messages, and leases each one for leaseSeconds. A message that is not
                //  acknowledged before its lease ends is delivered again.
                JsonElement received;
                try
                {
                    received = await Send(HttpMethod.Post, $"queues/{QueueName}/messages/receive",
                        new { consumer = Consumer, max = 10, waitSeconds = untilIdle ? 7 : 20, leaseSeconds = 60 }, stop.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var messages = received.GetProperty("messages").EnumerateArray().ToList();
                if (messages.Count == 0)
                {
                    if (untilIdle)
                    {
                        break;
                    }
                    continue;
                }

                var acks = messages.Select(message => new
                {
                    leaseId = message.GetProperty("leaseId").GetString(),
                    disposition = Handle(message)
                }).ToList();

                //One request acknowledges them all.
                await Send(HttpMethod.Post, "leases/ack", new { acks });
            }
        }

        private static string Handle(JsonElement message)
        {
            var source = message.TryGetProperty("headers", out var headers) && headers.TryGetProperty("source", out var s) ? s.GetString() : "unknown";
            var attempt = message.GetProperty("deliveryAttempt").GetInt32();

            switch (message.GetProperty("messageType").GetString())
            {
                case "orders.created":
                    //A JSON body arrives as JSON, so it deserializes directly.
                    var order = message.GetProperty("body").Deserialize<OrderCreated>(_json)!;
                    if (string.IsNullOrEmpty(order.Customer))
                    {
                        Console.WriteLine($"Order {order.OrderId} from {source} has no customer: dead-lettering it.");
                        return "deadLetter";
                    }
                    Console.WriteLine($"Order {order.OrderId} for {order.Customer}, {order.Total:0.00}, from {source} (attempt {attempt}).");
                    return "consumed";

                case "orders.note":
                    Console.WriteLine($"Note from {source}: {message.GetProperty("body").GetString()}");
                    return "consumed";

                case "orders.receipt":
                    var bytes = Convert.FromBase64String(message.GetProperty("bodyBase64").GetString()!);
                    Console.WriteLine($"Receipt from {source}: {bytes.Length} bytes of {message.GetProperty("contentType").GetString()}, starting \"{Encoding.ASCII.GetString(bytes, 0, Math.Min(8, bytes.Length))}\".");
                    return "consumed";

                default:
                    return "notInterested"; //Not for this consumer. Another one may want it.
            }
        }

        /// <summary>
        /// Sends a request and returns its JSON. Errors are problem details documents with a stable code. A full queue
        /// answers 429 queue_full with Retry-After, so this waits and tries again.
        /// </summary>
        private static async Task<JsonElement> Send(HttpMethod method, string path, object? body, CancellationToken cancellationToken = default)
        {
            while (true)
            {
                using var request = new HttpRequestMessage(method, path) { Content = body == null ? null : JsonContent.Create(body, options: _json) };
                using var response = await _http.SendAsync(request, cancellationToken);
                var text = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
                    Console.WriteLine($"The queue is full; trying again in {delay.TotalSeconds:0} seconds.");
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                if (response.IsSuccessStatusCode == false)
                {
                    var problem = text.Length > 0 ? JsonDocument.Parse(text).RootElement : default;
                    var code = problem.ValueKind == JsonValueKind.Object && problem.TryGetProperty("code", out var c) ? c.GetString() : null;
                    var title = problem.ValueKind == JsonValueKind.Object && problem.TryGetProperty("title", out var t) ? t.GetString() : null;
                    throw new HttpRequestException($"{method} {path} failed: {(int)response.StatusCode} {code} {title}".TrimEnd());
                }

                return text.Length > 0 ? JsonDocument.Parse(text).RootElement.Clone() : default;
            }
        }
    }
}
