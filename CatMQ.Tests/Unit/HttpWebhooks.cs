using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CatMQ.Tests.Unit
{
    /// <summary>
    /// Managing webhooks through the HTTP API, against the real service.
    /// </summary>
    public class HttpWebhooks(ServiceFixture service)
        : IClassFixture<ServiceFixture>, IDisposable
    {
        private readonly WebhookReceiver _receiver = new();
        private HttpClient Http => service.Http;

        public void Dispose() => _receiver.Dispose();

        private static async Task<JsonElement> Json(HttpResponseMessage response)
            => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

        private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
        {
            Assert.Equal(status, response.StatusCode);
            Assert.Equal(code, (await Json(response)).GetProperty("code").GetString());
        }

        private async Task<string> CreateQueue()
        {
            var queueName = "q" + Guid.NewGuid().ToString("N");
            Assert.Equal(HttpStatusCode.Created, (await Http.PutAsJsonAsync($"queues/{queueName}", new { })).StatusCode);
            return queueName;
        }

        private async Task<int> Depth(string queueName)
            => (await Json(await Http.GetAsync($"queues/{queueName}"))).GetProperty("stats").GetProperty("depth").GetInt32();

        [Fact(DisplayName = "HTTP webhooks: create, deliver, read back without header values, rotate, and delete.")]
        public async Task TestLifecycle()
        {
            var queueName = await CreateQueue();

            var created = await Http.PutAsJsonAsync($"queues/{queueName}/webhooks/orders", new
            {
                url = _receiver.Url + "orders",
                timeoutSeconds = 10,
                headers = new Dictionary<string, string> { ["Authorization"] = "Bearer receiver-token" }
            }, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var webhook = await Json(created);
            var secret = webhook.GetProperty("secret").GetString()!;
            Assert.StartsWith("whsec_", secret);
            Assert.Equal("orders", webhook.GetProperty("name").GetString());
            Assert.Equal(queueName, webhook.GetProperty("queue").GetString());
            Assert.Equal(10, webhook.GetProperty("timeoutSeconds").GetInt32());
            Assert.True(webhook.GetProperty("enabled").GetBoolean());
            Assert.Equal("********", webhook.GetProperty("headers").GetProperty("Authorization").GetString());

            var published = await Http.PostAsJsonAsync($"queues/{queueName}/messages", new { body = new { orderId = 9 } }, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Accepted, published.StatusCode);
            Assert.True(await Wait.UntilAsync(async () => await Depth(queueName) == 0));

            var delivery = Assert.Single(_receiver.Deliveries);
            Assert.True(WebhookReceiver.IsSignedWith(delivery, secret));
            Assert.Equal("Bearer receiver-token", delivery.Headers["Authorization"]);
            Assert.Equal(9, delivery.Json.GetProperty("body").GetProperty("orderId").GetInt32());

            //Reading it back never includes the secret, nor header values.
            var read = await Json(await Http.GetAsync($"queues/{queueName}/webhooks/orders", cancellationToken: TestContext.Current.CancellationToken));
            Assert.False(read.TryGetProperty("secret", out _));
            Assert.Equal("********", read.GetProperty("headers").GetProperty("Authorization").GetString());
            Assert.NotEqual(JsonValueKind.Null, read.GetProperty("lastSuccessAt").ValueKind);

            var list = await Json(await Http.GetAsync($"queues/{queueName}/webhooks", cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal("orders", Assert.Single(list.GetProperty("webhooks").EnumerateArray()).GetProperty("name").GetString());

            var consumers = await Json(await Http.GetAsync($"queues/{queueName}/consumers", cancellationToken: TestContext.Current.CancellationToken));
            var consumer = Assert.Single(consumers.GetProperty("consumers").EnumerateArray());
            Assert.Equal("orders", consumer.GetProperty("name").GetString());

            //Replacing the settings keeps the secret, so it answers 200 without one.
            var replaced = await Http.PutAsJsonAsync($"queues/{queueName}/webhooks/orders", new { url = _receiver.Url + "orders", enabled = false }, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
            Assert.False((await Json(replaced)).TryGetProperty("secret", out _));

            var rotated = await Http.PostAsync($"queues/{queueName}/webhooks/orders/rotate-secret", null, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
            var newSecret = (await Json(rotated)).GetProperty("secret").GetString();
            Assert.StartsWith("whsec_", newSecret);
            Assert.NotEqual(secret, newSecret);

            Assert.Equal(HttpStatusCode.NoContent, (await Http.DeleteAsync($"queues/{queueName}/webhooks/orders", cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
            await AssertProblem(await Http.GetAsync($"queues/{queueName}/webhooks/orders", cancellationToken: TestContext.Current.CancellationToken), HttpStatusCode.NotFound, "webhook_not_found");
            await AssertProblem(await Http.PostAsync($"queues/{queueName}/webhooks/orders/rotate-secret", null, cancellationToken: TestContext.Current.CancellationToken), HttpStatusCode.NotFound, "webhook_not_found");
            Assert.Empty((await Json(await Http.GetAsync($"queues/{queueName}/webhooks", cancellationToken: TestContext.Current.CancellationToken))).GetProperty("webhooks").EnumerateArray());
        }

        [Fact(DisplayName = "HTTP webhooks: deleting the consumer deletes the webhook.")]
        public async Task TestDeleteAsConsumer()
        {
            var queueName = await CreateQueue();
            Assert.Equal(HttpStatusCode.Created, (await Http.PutAsJsonAsync($"queues/{queueName}/webhooks/hook", new { url = _receiver.Url }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);

            Assert.Equal(HttpStatusCode.NoContent, (await Http.DeleteAsync($"queues/{queueName}/consumers/hook", cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
            Assert.Empty((await Json(await Http.GetAsync($"queues/{queueName}/webhooks", cancellationToken: TestContext.Current.CancellationToken))).GetProperty("webhooks").EnumerateArray());
        }

        [Fact(DisplayName = "HTTP webhooks: invalid requests are problem details.")]
        public async Task TestValidation()
        {
            var queueName = await CreateQueue();

            await AssertProblem(await Http.PutAsJsonAsync($"queues/{queueName}/webhooks/hook", new { }, cancellationToken: TestContext.Current.CancellationToken), HttpStatusCode.BadRequest, "invalid_request");
            await AssertProblem(await Http.PutAsJsonAsync($"queues/{queueName}/webhooks/hook", new { url = "not a url" }, cancellationToken: TestContext.Current.CancellationToken), HttpStatusCode.BadRequest, "invalid_request");
            await AssertProblem(await Http.PutAsJsonAsync($"queues/{queueName}/webhooks/hook", new { url = _receiver.Url, timeoutSeconds = 0 }, cancellationToken: TestContext.Current.CancellationToken), HttpStatusCode.BadRequest, "invalid_request");
            await AssertProblem(await Http.PutAsJsonAsync($"queues/{queueName}/webhooks/bad%20name", new { url = _receiver.Url }, cancellationToken: TestContext.Current.CancellationToken), HttpStatusCode.BadRequest, "invalid_request");
            await AssertProblem(await Http.PutAsJsonAsync($"queues/q{Guid.NewGuid():N}/webhooks/hook", new { url = _receiver.Url }, cancellationToken: TestContext.Current.CancellationToken), HttpStatusCode.NotFound, "queue_not_found");
            await AssertProblem(await Http.GetAsync($"queues/q{Guid.NewGuid():N}/webhooks", cancellationToken: TestContext.Current.CancellationToken), HttpStatusCode.NotFound, "queue_not_found");

            //A lease consumer's name cannot also be a webhook's.
            Assert.Equal(HttpStatusCode.OK, (await Http.PostAsJsonAsync($"queues/{queueName}/messages/receive", new { consumer = "worker", waitSeconds = 0 }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
            await AssertProblem(await Http.PutAsJsonAsync($"queues/{queueName}/webhooks/worker", new { url = _receiver.Url }, cancellationToken: TestContext.Current.CancellationToken), HttpStatusCode.BadRequest, "invalid_request");
        }
    }
}
