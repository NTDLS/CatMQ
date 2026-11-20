using NTDLS.CatMQ.Server;
using NTDLS.CatMQ.Shared;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace CatMQ.Tests.Unit
{
    /// <summary>
    /// Webhooks, which POST each message to a URL. These use their own server, because it has to allow http URLs.
    /// </summary>
    public class Webhooks : IDisposable
    {
        private readonly string _path = Path.Join(Path.GetTempPath(), "CatMQ.Tests", Guid.NewGuid().ToString("N"));
        private readonly int _port;
        private CMqServer _server;
        private readonly WebhookReceiver _receiver = new();

        public Webhooks()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            _port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            _server = StartServer();
        }

        private CMqServer StartServer()
        {
            var server = new CMqServer(new CMqServerConfiguration { PersistencePath = _path, AllowHttpWebhooks = true });
            server.Start(_port);
            return server;
        }

        public void Dispose()
        {
            _server.Stop();
            _receiver.Dispose();
            try { Directory.Delete(_path, true); } catch { }
        }

        private static string NewQueueName() => Guid.NewGuid().ToString("N");

        private int Depth(string queueName) => _server.GetQueue(queueName)?.QueueDepth ?? -1;

        private string CreateQueueWithWebhook(CMqQueueConfiguration? configuration = null, CMqWebhookSettings? settings = null, string name = "hook")
        {
            configuration ??= new CMqQueueConfiguration(NewQueueName());
            _server.CreateQueue(configuration);
            settings ??= new CMqWebhookSettings();
            settings.Url = string.IsNullOrEmpty(settings.Url) ? _receiver.Url + "hook" : settings.Url;
            _server.PutWebhook(configuration.QueueName, name, settings, out var secret);
            Assert.NotNull(secret);
            LastSecret = secret!;
            return configuration.QueueName;
        }

        private string LastSecret { get; set; } = string.Empty;

        [Fact(DisplayName = "Webhook: a message is POSTed, signed, and consumed by a 2xx response.")]
        public void TestDelivery()
        {
            var queueName = CreateQueueWithWebhook(settings: new CMqWebhookSettings { Headers = new() { ["Authorization"] = "Bearer receiver-token" } });
            var messageId = _server.Enqueue(queueName, null, "orders.created", "{\"orderId\":7}", null, new() { ["tenant"] = "acme" });

            Assert.True(Wait.Until(() => Depth(queueName) == 0));
            var delivery = Assert.Single(_receiver.Deliveries);

            Assert.Equal("/hook", delivery.Path);
            Assert.True(WebhookReceiver.IsSignedWith(delivery, LastSecret));
            Assert.False(WebhookReceiver.IsSignedWith(delivery, "whsec_wrong"));
            Assert.Equal(messageId.ToString(), delivery.Headers["X-CatMQ-Message-Id"]);
            Assert.Equal("Bearer receiver-token", delivery.Headers["Authorization"]);
            Assert.StartsWith("application/json", delivery.Headers["Content-Type"]);

            var json = delivery.Json;
            Assert.Equal(queueName, json.GetProperty("queue").GetString());
            Assert.Equal(delivery.Headers["X-CatMQ-Delivery-Id"], json.GetProperty("deliveryId").GetString());
            Assert.Equal(messageId, json.GetProperty("messageId").GetGuid());
            Assert.Equal("orders.created", json.GetProperty("messageType").GetString());
            Assert.Equal(7, json.GetProperty("body").GetProperty("orderId").GetInt32());
            Assert.Equal("acme", json.GetProperty("headers").GetProperty("tenant").GetString());
            Assert.Equal(1, json.GetProperty("deliveryAttempt").GetInt32());
            Assert.Equal(JsonValueKind.String, json.GetProperty("serialNumber").ValueKind);

            var webhook = _server.GetWebhook(queueName, "hook")!;
            Assert.NotNull(webhook.LastSuccessAt);
            Assert.Equal(0, webhook.ConsecutiveFailures);
            Assert.Equal("webhook", Assert.Single(_server.GetSubscribers(queueName)!).TransportName);
        }

        [Fact(DisplayName = "Webhook: text and binary bodies are delivered as they are in the HTTP API.")]
        public void TestBodyFormats()
        {
            var queueName = CreateQueueWithWebhook(new CMqQueueConfiguration(NewQueueName()) { ConsumptionScheme = CMqConsumptionScheme.FirstConsumedSubscriber, AsynchronousDelivery = false });
            _server.Enqueue(queueName, null, null, "hello", "text/plain");
            _server.Enqueue(queueName, null, null, Convert.ToBase64String([1, 2, 3]), "application/octet-stream", null, bodyIsBase64: true);

            Assert.True(Wait.Until(() => _receiver.Deliveries.Count == 2));
            var deliveries = _receiver.Deliveries.ToArray();
            Assert.Equal("hello", deliveries[0].Json.GetProperty("body").GetString());
            Assert.Equal(Convert.ToBase64String([1, 2, 3]), deliveries[1].Json.GetProperty("bodyBase64").GetString());
        }

        [Fact(DisplayName = "Webhook: 410 drops a message and 422 dead-letters it.")]
        public void TestDropAndDeadLetter()
        {
            _receiver.Respond = d => new WebhookReceiver.Response(d.Json.GetProperty("body").GetProperty("n").GetInt32() == 1 ? 410 : 422);
            var queueName = CreateQueueWithWebhook(new CMqQueueConfiguration(NewQueueName()) { DeadLetterConfiguration = new CMqDeadLetterQueueConfiguration() });

            _server.Enqueue(queueName, null, null, "{\"n\":1}");
            _server.Enqueue(queueName, null, null, "{\"n\":2}");

            Assert.True(Wait.Until(() => Depth(queueName) == 0 && Depth($"{queueName}.dlq") == 1));
            Assert.Equal(0UL, _server.GetQueue(queueName)?.FailedDeliveryCount);
        }

        [Fact(DisplayName = "Webhook: 503 with Retry-After defers a message, which is not a failure.")]
        public void TestDefer()
        {
            int calls = 0;
            _receiver.Respond = _ => Interlocked.Increment(ref calls) == 1 ? new WebhookReceiver.Response(503, RetryAfterSeconds: 1) : new WebhookReceiver.Response(200);
            var queueName = CreateQueueWithWebhook();

            _server.Enqueue(queueName, null, null, "{}");

            Assert.True(Wait.Until(() => Depth(queueName) == 0));
            var deliveries = _receiver.Deliveries.ToArray();
            Assert.Equal(2, deliveries.Length);
            Assert.True(deliveries[1].ReceivedAt - deliveries[0].ReceivedAt >= TimeSpan.FromMilliseconds(900), "Redelivered before Retry-After.");
            Assert.Equal(0UL, _server.GetQueue(queueName)?.FailedDeliveryCount);
            Assert.Equal(1UL, _server.GetQueue(queueName)?.DeferredDeliveryCount);
        }

        [Fact(DisplayName = "Webhook: an error response is a failed delivery, which is retried and recorded.")]
        public void TestFailureIsRetried()
        {
            //The third attempt is held until the test has seen the two failures, because its success resets the count,
            //  and it follows the second failure within milliseconds.
            int calls = 0;
            using var release = new ManualResetEventSlim();
            _receiver.Respond = _ =>
            {
                if (Interlocked.Increment(ref calls) <= 2)
                {
                    return new WebhookReceiver.Response(500);
                }
                release.Wait(TimeSpan.FromSeconds(30));
                return new WebhookReceiver.Response(200);
            };
            var queueName = CreateQueueWithWebhook();

            _server.Enqueue(queueName, null, null, "{}");
            Assert.True(Wait.Until(() => _server.GetWebhook(queueName, "hook")?.ConsecutiveFailures == 2));
            Assert.Contains("500", _server.GetWebhook(queueName, "hook")!.LastFailure);
            release.Set();

            Assert.True(Wait.Until(() => Depth(queueName) == 0));
            Assert.Equal(3, _receiver.Deliveries.Last().Json.GetProperty("deliveryAttempt").GetInt32());
            Assert.Equal(2UL, _server.GetQueue(queueName)?.FailedDeliveryCount);
            Assert.Equal(0, _server.GetWebhook(queueName, "hook")!.ConsecutiveFailures);
        }

        [Fact(DisplayName = "Webhook: no response within the timeout is a failed delivery.")]
        public void TestTimeout()
        {
            int calls = 0;
            _receiver.Respond = _ => Interlocked.Increment(ref calls) == 1 ? new WebhookReceiver.Response(200, DelayMilliseconds: 3000) : new WebhookReceiver.Response(200);
            var queueName = CreateQueueWithWebhook(settings: new CMqWebhookSettings { TimeoutSeconds = 1 });

            _server.Enqueue(queueName, null, null, "{}");

            Assert.True(Wait.Until(() => Depth(queueName) == 0));
            Assert.Contains("No response within 1 seconds", _server.GetWebhook(queueName, "hook")!.LastFailure);
            Assert.Equal(1UL, _server.GetQueue(queueName)?.FailedDeliveryCount);
        }

        [Fact(DisplayName = "Webhook: redirects are not followed.")]
        public void TestRedirectNotFollowed()
        {
            _receiver.Respond = d => d.Path == "/hook" ? new WebhookReceiver.Response(302, Location: _receiver.Url + "elsewhere") : new WebhookReceiver.Response(200);
            var queueName = CreateQueueWithWebhook(new CMqQueueConfiguration(NewQueueName()) { MaxDeliveryAttempts = 1, DeadLetterConfiguration = new CMqDeadLetterQueueConfiguration() });

            _server.Enqueue(queueName, null, null, "{}");

            Assert.True(Wait.Until(() => _server.GetQueue(queueName)?.FailedDeliveryCount >= 1));
            Thread.Sleep(300);
            Assert.DoesNotContain(_receiver.Deliveries, o => o.Path == "/elsewhere");
            Assert.Contains("302", _server.GetWebhook(queueName, "hook")!.LastFailure);
        }

        [Fact(DisplayName = "Webhook: no more deliveries are in flight than its maximum concurrency.")]
        public void TestMaxConcurrency()
        {
            _receiver.Respond = _ => new WebhookReceiver.Response(200, DelayMilliseconds: 200);
            var queueName = CreateQueueWithWebhook(new CMqQueueConfiguration(NewQueueName()) { MaxOutstandingDeliveries = 10 },
                new CMqWebhookSettings { MaxConcurrency = 2 });

            for (int i = 0; i < 10; i++)
            {
                _server.Enqueue(queueName, null, null, "{}");
            }

            Assert.True(Wait.Until(() => Depth(queueName) == 0, 20));
            Assert.Equal(2, _receiver.MaxConcurrent);
        }

        [Fact(DisplayName = "Webhook: replacing settings keeps the secret, and a disabled webhook receives nothing.")]
        public void TestReplaceAndDisable()
        {
            var queueName = CreateQueueWithWebhook();
            var secret = LastSecret;

            _server.PutWebhook(queueName, "hook", new CMqWebhookSettings { Url = _receiver.Url + "hook", Enabled = false }, out var newSecret);
            Assert.Null(newSecret);

            _server.Enqueue(queueName, null, null, "{}");
            Thread.Sleep(400);
            Assert.Empty(_receiver.Deliveries);
            Assert.Equal(1, Depth(queueName));

            _server.PutWebhook(queueName, "hook", new CMqWebhookSettings { Url = _receiver.Url + "hook" }, out _);
            Assert.True(Wait.Until(() => Depth(queueName) == 0));
            Assert.True(WebhookReceiver.IsSignedWith(Assert.Single(_receiver.Deliveries), secret));
        }

        [Fact(DisplayName = "Webhook: after rotating the secret, deliveries are signed with both the new and the old one.")]
        public void TestRotateSecret()
        {
            var queueName = CreateQueueWithWebhook();
            var oldSecret = LastSecret;

            var newSecret = _server.RotateWebhookSecret(queueName, "hook");
            Assert.NotNull(newSecret);
            Assert.NotEqual(oldSecret, newSecret);
            Assert.Null(_server.RotateWebhookSecret(queueName, "missing"));

            _server.Enqueue(queueName, null, null, "{}");
            Assert.True(Wait.Until(() => Depth(queueName) == 0));

            var delivery = Assert.Single(_receiver.Deliveries);
            Assert.True(WebhookReceiver.IsSignedWith(delivery, newSecret!));
            Assert.True(WebhookReceiver.IsSignedWith(delivery, oldSecret));
        }

        [Fact(DisplayName = "Webhook: a deleted webhook receives nothing more.")]
        public void TestDelete()
        {
            var queueName = CreateQueueWithWebhook();
            Assert.True(_server.DeleteWebhook(queueName, "hook"));
            Assert.False(_server.DeleteWebhook(queueName, "hook"));
            Assert.Empty(_server.GetWebhooks(queueName));
            Assert.Empty(_server.GetSubscribers(queueName)!);

            _server.Enqueue(queueName, null, null, "{}");
            Thread.Sleep(300);
            Assert.Empty(_receiver.Deliveries);
        }

        [Fact(DisplayName = "Webhook: webhooks and their secrets survive a restart.")]
        public void TestSurvivesRestart()
        {
            var queueName = CreateQueueWithWebhook(new CMqQueueConfiguration(NewQueueName()) { PersistenceScheme = CMqPersistenceScheme.Persistent },
                new CMqWebhookSettings { Enabled = false, TimeoutSeconds = 9 });
            var secret = LastSecret;
            _server.Enqueue(queueName, null, null, "{\"kept\":true}");

            _server.Stop();
            _server = StartServer();

            var webhook = _server.GetWebhook(queueName, "hook")!;
            Assert.Equal(9, webhook.Settings.TimeoutSeconds);
            Assert.False(webhook.Settings.Enabled);

            _server.PutWebhook(queueName, "hook", new CMqWebhookSettings { Url = _receiver.Url + "hook" }, out _);
            Assert.True(Wait.Until(() => Depth(queueName) == 0));
            var delivery = Assert.Single(_receiver.Deliveries);
            Assert.True(delivery.Json.GetProperty("body").GetProperty("kept").GetBoolean());
            Assert.True(WebhookReceiver.IsSignedWith(delivery, secret));
        }

        [Fact(DisplayName = "Webhook: invalid settings and name clashes are refused.")]
        public async Task TestValidation()
        {
            var queueName = NewQueueName();
            _server.CreateQueue(new CMqQueueConfiguration(queueName));

            Assert.Throws<ArgumentException>(() => _server.PutWebhook(queueName, "bad name", new CMqWebhookSettings { Url = _receiver.Url }, out _));
            Assert.Throws<ArgumentException>(() => _server.PutWebhook(queueName, "hook", new CMqWebhookSettings { Url = "ftp://example.com" }, out _));
            Assert.Throws<ArgumentException>(() => _server.PutWebhook(queueName, "hook", new CMqWebhookSettings { Url = "relative/path" }, out _));
            Assert.Throws<ArgumentException>(() => _server.PutWebhook(queueName, "hook", new CMqWebhookSettings { Url = _receiver.Url, TimeoutSeconds = 0 }, out _));
            Assert.Throws<ArgumentException>(() => _server.PutWebhook(queueName, "hook", new CMqWebhookSettings { Url = _receiver.Url, MaxConcurrency = 0 }, out _));
            Assert.Throws<ArgumentException>(() => _server.PutWebhook(queueName, "hook", new CMqWebhookSettings { Url = _receiver.Url, Headers = new() { ["X-Bad"] = "a\r\nb" } }, out _));
            Assert.Throws<CMqQueueNotFoundException>(() => _server.PutWebhook(NewQueueName(), "hook", new CMqWebhookSettings { Url = _receiver.Url }, out _));

            //A webhook and a lease consumer cannot share a name, because they would compete for messages without meaning to.
            Assert.Empty(await _server.ReceiveAsync(queueName, "shared", 1, TimeSpan.Zero, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Throws<ArgumentException>(() => _server.PutWebhook(queueName, "shared", new CMqWebhookSettings { Url = _receiver.Url }, out _));
            _server.PutWebhook(queueName, "hook", new CMqWebhookSettings { Url = _receiver.Url }, out _);
            await Assert.ThrowsAnyAsync<ArgumentException>(() => _server.ReceiveAsync(queueName, "hook", 1, TimeSpan.Zero, cancellationToken: TestContext.Current.CancellationToken));
        }

        [Fact(DisplayName = "Webhook: https is required unless the server allows http.")]
        public void TestHttpsRequiredByDefault()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            var server = new CMqServer();
            server.Start(port);
            try
            {
                server.CreateQueue(new CMqQueueConfiguration("q"));
                var ex = Assert.Throws<ArgumentException>(() => server.PutWebhook("q", "hook", new CMqWebhookSettings { Url = "http://example.com/hook" }, out _));
                Assert.Contains("https", ex.Message);
                server.PutWebhook("q", "hook", new CMqWebhookSettings { Url = "https://example.com/hook", Enabled = false }, out var secret);
                Assert.StartsWith("whsec_", secret);
            }
            finally
            {
                server.Stop();
            }
        }
    }
}
