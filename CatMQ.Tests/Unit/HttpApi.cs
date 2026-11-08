using CatMQ.Tests.Messages;
using NTDLS.CatMQ.Client;
using NTDLS.CatMQ.Shared;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace CatMQ.Tests.Unit
{
    /// <summary>
    /// Tests the HTTP API from the outside, over HTTP against the real service, as a client in any language would use it.
    /// </summary>
    public class HttpApi(ServiceFixture service)
        : IClassFixture<ServiceFixture>
    {
        private HttpClient Http => service.Http;

        private static string NewQueueName() => "q" + Guid.NewGuid().ToString("N");

        private static async Task<JsonElement> Json(HttpResponseMessage response)
        {
            var text = await response.Content.ReadAsStringAsync();
            return JsonDocument.Parse(text).RootElement.Clone();
        }

        private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
        {
            Assert.Equal(status, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var problem = await Json(response);
            Assert.Equal(code, problem.GetProperty("code").GetString());
            Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
            Assert.False(string.IsNullOrEmpty(problem.GetProperty("title").GetString()));
        }

        private async Task<string> CreateQueue(object? settings = null)
        {
            var queueName = NewQueueName();
            var response = await Http.PutAsJsonAsync($"queues/{queueName}", settings ?? new { });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return queueName;
        }

        private async Task<JsonElement> Publish(string queueName, object message)
        {
            var response = await Http.PostAsJsonAsync($"queues/{queueName}/messages", message);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            return (await Json(response)).GetProperty("results");
        }

        private async Task<JsonElement[]> Receive(string queueName, string consumer = "worker", int max = 1, int waitSeconds = 5, int leaseSeconds = 30)
        {
            var response = await Http.PostAsJsonAsync($"queues/{queueName}/messages/receive", new { consumer, max, waitSeconds, leaseSeconds });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await Json(response)).GetProperty("messages").EnumerateArray().ToArray();
        }

        /// <summary>
        /// A receive returns the messages that arrive quickly, which is not always every free slot at once, so this keeps receiving.
        /// </summary>
        private async Task<JsonElement[]> ReceiveAtLeast(string queueName, int count, int max)
        {
            var received = new List<JsonElement>();
            while (received.Count < count)
            {
                var batch = await Receive(queueName, max: max - received.Count);
                Assert.NotEmpty(batch);
                received.AddRange(batch);
            }
            return received.ToArray();
        }

        private async Task Ack(JsonElement message, string disposition = "consumed")
        {
            var response = await Http.PostAsJsonAsync($"leases/{message.GetProperty("leaseId").GetString()}/ack", new { disposition });
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        private async Task<JsonElement> Stats(string queueName)
            => (await Json(await Http.GetAsync($"queues/{queueName}"))).GetProperty("stats");

        private async Task<bool> WaitForDepth(string queueName, int depth)
        {
            for (int i = 0; i < 250; i++)
            {
                if ((await Stats(queueName)).GetProperty("depth").GetInt32() == depth)
                {
                    return true;
                }
                await Task.Delay(20);
            }
            return false;
        }

        #region Authentication and system.

        [Fact(DisplayName = "HTTP: health needs no key, and everything else does.")]
        public async Task TestAuthentication()
        {
            using var anonymous = new HttpClient { BaseAddress = Http.BaseAddress };

            var health = await anonymous.GetAsync("health");
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
            Assert.Equal("ok", (await Json(health)).GetProperty("status").GetString());

            await AssertProblem(await anonymous.GetAsync("queues"), HttpStatusCode.Unauthorized, "unauthorized");

            anonymous.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong");
            await AssertProblem(await anonymous.GetAsync("queues"), HttpStatusCode.Unauthorized, "unauthorized");

            anonymous.DefaultRequestHeaders.Authorization = null;
            anonymous.DefaultRequestHeaders.Add("x-catmq-api-key", ServiceFixture.ApiKey);
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("queues")).StatusCode);
        }

        #endregion

        #region Queues.

        [Fact(DisplayName = "HTTP: PUT creates a queue, applies new settings, and refuses to change persistence.")]
        public async Task TestPutQueue()
        {
            var queueName = await CreateQueue(new { persistence = "persistent", consumptionScheme = "firstConsumer", maxOutstandingDeliveries = 3 });

            var queue = await Json(await Http.GetAsync($"queues/{queueName}"));
            Assert.Equal(queueName, queue.GetProperty("name").GetString());
            Assert.Equal("persistent", queue.GetProperty("settings").GetProperty("persistence").GetString());
            Assert.Equal("firstConsumer", queue.GetProperty("settings").GetProperty("consumptionScheme").GetString());
            Assert.Equal(3, queue.GetProperty("settings").GetProperty("maxOutstandingDeliveries").GetInt32());
            Assert.Equal(JsonValueKind.String, queue.GetProperty("stats").GetProperty("receivedCount").ValueKind);

            var updated = await Http.PutAsJsonAsync($"queues/{queueName}", new { persistence = "persistent", consumptionScheme = "firstConsumer", maxOutstandingDeliveries = 9 });
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            Assert.Equal(9, (await Json(updated)).GetProperty("settings").GetProperty("maxOutstandingDeliveries").GetInt32());

            await AssertProblem(await Http.PutAsJsonAsync($"queues/{queueName}", new { persistence = "ephemeral" }),
                HttpStatusCode.Conflict, "queue_persistence_immutable");
        }

        [Fact(DisplayName = "HTTP: PATCH changes only what is supplied.")]
        public async Task TestPatchQueue()
        {
            var queueName = await CreateQueue(new { maxOutstandingDeliveries = 3, maxDeliveryAttempts = 7, maxMessageAgeSeconds = 100 });

            var patched = await Json(await Http.PatchAsJsonAsync($"queues/{queueName}", new { maxOutstandingDeliveries = 8 }));
            var settings = patched.GetProperty("settings");
            Assert.Equal(8, settings.GetProperty("maxOutstandingDeliveries").GetInt32());
            Assert.Equal(7, settings.GetProperty("maxDeliveryAttempts").GetInt32());
            Assert.Equal(100, settings.GetProperty("maxMessageAgeSeconds").GetInt32());

            var cleared = await Json(await Http.PatchAsJsonAsync($"queues/{queueName}", new { maxMessageAgeSeconds = (int?)null }));
            Assert.Equal(JsonValueKind.Null, cleared.GetProperty("settings").GetProperty("maxMessageAgeSeconds").ValueKind);

            await AssertProblem(await Http.PatchAsJsonAsync($"queues/{NewQueueName()}", new { maxOutstandingDeliveries = 8 }),
                HttpStatusCode.NotFound, "queue_not_found");
        }

        [Fact(DisplayName = "HTTP: bad requests get a consistent error.")]
        public async Task TestBadRequests()
        {
            var queueName = await CreateQueue();

            await AssertProblem(await Http.PutAsJsonAsync($"queues/{NewQueueName()}", new { persistence = "sometimes" }), HttpStatusCode.BadRequest, "invalid_request");
            await AssertProblem(await Http.PutAsJsonAsync($"queues/{NewQueueName()}", new { maxOutstandingDeliveries = 0 }), HttpStatusCode.BadRequest, "invalid_request");
            await AssertProblem(await Http.PutAsJsonAsync("queues/name.dlq", new { }), HttpStatusCode.BadRequest, "invalid_request");
            await AssertProblem(await Http.GetAsync($"queues/{NewQueueName()}"), HttpStatusCode.NotFound, "queue_not_found");

            var malformed = await Http.PutAsync($"queues/{NewQueueName()}", new StringContent("{not json", Encoding.UTF8, "application/json"));
            await AssertProblem(malformed, HttpStatusCode.BadRequest, "invalid_request");

            await AssertProblem(await Http.PostAsJsonAsync($"queues/{queueName}/messages/receive", new { max = 1 }), HttpStatusCode.BadRequest, "invalid_request");
            await AssertProblem(await Http.PostAsJsonAsync($"queues/{queueName}/messages/receive", new { consumer = "bad name" }), HttpStatusCode.BadRequest, "invalid_request");
            await AssertProblem(await Http.PostAsJsonAsync($"queues/{queueName}/messages/receive", new { consumer = "w", waitSeconds = 61 }), HttpStatusCode.BadRequest, "invalid_request");
            await AssertProblem(await Http.GetAsync("queues?limit=0"), HttpStatusCode.BadRequest, "invalid_request");
        }

        [Fact(DisplayName = "HTTP: queues can be listed a page at a time, and deleted repeatedly.")]
        public async Task TestListAndDeleteQueues()
        {
            var created = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                created.Add(await CreateQueue());
            }

            var seen = new List<string>();
            string? cursor = null;
            do
            {
                var page = await Json(await Http.GetAsync("queues?limit=2" + (cursor == null ? "" : $"&cursor={cursor}")));
                Assert.True(page.GetProperty("queues").GetArrayLength() <= 2);
                seen.AddRange(page.GetProperty("queues").EnumerateArray().Select(o => o.GetProperty("name").GetString()!));
                cursor = page.GetProperty("nextCursor").GetString();
            } while (cursor != null);

            Assert.Equal(seen.Count, seen.Distinct().Count());
            Assert.All(created, o => Assert.Contains(o, seen));

            Assert.Equal(HttpStatusCode.NoContent, (await Http.DeleteAsync($"queues/{created[0]}")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Http.DeleteAsync($"queues/{created[0]}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Http.GetAsync($"queues/{created[0]}")).StatusCode);
        }

        #endregion

        #region Publishing.

        [Theory(DisplayName = "HTTP: JSON, text and binary bodies come back as they were sent.")]
        [InlineData("ephemeral")]
        [InlineData("persistent")]
        public async Task TestBodies(string persistence)
        {
            var queueName = await CreateQueue(new { persistence, consumptionScheme = "firstConsumer" });
            byte[] bytes = [0, 1, 2, 254, 255];

            await Publish(queueName, new { messageType = "orders.created", body = new { orderId = 1234, items = new[] { "a", "b" } }, headers = new { tenant = "acme" } });
            await Publish(queueName, new { contentType = "text/plain", body = "hello there" });
            await Publish(queueName, new { contentType = "application/octet-stream", bodyBase64 = Convert.ToBase64String(bytes) });

            var json = (await Receive(queueName)).Single();
            Assert.Equal("orders.created", json.GetProperty("messageType").GetString());
            Assert.Equal("application/json", json.GetProperty("contentType").GetString());
            Assert.Equal(1234, json.GetProperty("body").GetProperty("orderId").GetInt32());
            Assert.Equal("b", json.GetProperty("body").GetProperty("items")[1].GetString());
            Assert.Equal("acme", json.GetProperty("headers").GetProperty("tenant").GetString());
            Assert.Equal(1, json.GetProperty("deliveryAttempt").GetInt32());
            Assert.Equal(JsonValueKind.String, json.GetProperty("serialNumber").ValueKind);
            await Ack(json);

            var text = (await Receive(queueName)).Single();
            Assert.Equal("hello there", text.GetProperty("body").GetString());
            await Ack(text);

            var binary = (await Receive(queueName)).Single();
            Assert.Equal(bytes, Convert.FromBase64String(binary.GetProperty("bodyBase64").GetString()!));
            Assert.False(binary.TryGetProperty("body", out _));
            await Ack(binary);

            Assert.True(await WaitForDepth(queueName, 0));
        }

        [Fact(DisplayName = "HTTP: a batch reports each message, and invalid publishes are refused.")]
        public async Task TestPublishValidation()
        {
            var queueName = await CreateQueue();

            var results = await Publish(queueName, new object[] { }.Length == 0 ? new
            {
                messages = new object[]
                {
                    new { body = new { n = 1 } },
                    new { messageType = "bad" },
                    new { contentType = "text/plain", body = 5 },
                    new { bodyBase64 = "@@@" },
                    new { body = new { n = 2 } }
                }
            } : null!);

            Assert.Equal(["accepted", "rejected", "rejected", "rejected", "accepted"], results.EnumerateArray().Select(o => o.GetProperty("status").GetString()).ToArray());
            Assert.Equal("invalid_request", results[1].GetProperty("error").GetProperty("code").GetString());
            Assert.Equal(2, (await Stats(queueName)).GetProperty("depth").GetInt32());

            await AssertProblem(await Http.PostAsJsonAsync($"queues/{queueName}/messages", new { messageType = "nobody" }), HttpStatusCode.BadRequest, "invalid_request");
            await AssertProblem(await Http.PostAsJsonAsync($"queues/{queueName}/messages", new { body = 1, bodyBase64 = "AA==" }), HttpStatusCode.BadRequest, "invalid_request");
            await AssertProblem(await Http.PostAsJsonAsync($"queues/{queueName}/messages", new { messages = Array.Empty<object>() }), HttpStatusCode.BadRequest, "invalid_request");
            await AssertProblem(await Http.PostAsJsonAsync($"queues/{NewQueueName()}/messages", new { body = 1 }), HttpStatusCode.NotFound, "queue_not_found");
        }

        [Fact(DisplayName = "HTTP: an idempotency key makes retries safe, including concurrent ones.")]
        public async Task TestIdempotency()
        {
            var queueName = await CreateQueue();

            var first = await Publish(queueName, new { idempotencyKey = "order-1", body = 1 });
            var repeat = await Publish(queueName, new { idempotencyKey = "order-1", body = 1 });

            Assert.Equal("accepted", first[0].GetProperty("status").GetString());
            Assert.Equal("duplicate", repeat[0].GetProperty("status").GetString());
            Assert.Equal(first[0].GetProperty("messageId").GetString(), repeat[0].GetProperty("messageId").GetString());
            Assert.Equal(1, (await Stats(queueName)).GetProperty("depth").GetInt32());

            var concurrent = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Publish(queueName, new { idempotencyKey = "order-2", body = 2 })));
            Assert.Single(concurrent, o => o[0].GetProperty("status").GetString() == "accepted");
            Assert.Single(concurrent.Select(o => o[0].GetProperty("messageId").GetString()).Distinct());
            Assert.Equal(2, (await Stats(queueName)).GetProperty("depth").GetInt32());

            //Keys belong to a queue.
            var otherQueue = await CreateQueue();
            Assert.Equal("accepted", (await Publish(otherQueue, new { idempotencyKey = "order-1", body = 1 }))[0].GetProperty("status").GetString());
        }

        [Fact(DisplayName = "HTTP: a deferred message is not delivered until its time.")]
        public async Task TestDeferredPublish()
        {
            var queueName = await CreateQueue();
            await Publish(queueName, new { deferSeconds = 2, body = 1 });

            Assert.Empty(await Receive(queueName, waitSeconds: 1));
            await Ack((await Receive(queueName, waitSeconds: 5)).Single());
        }

        #endregion

        [Fact(DisplayName = "HTTP: a full queue refuses messages with 429, and the limit can be changed.")]
        public async Task TestQueueFull()
        {
            var queueName = await CreateQueue(new { maxDepth = 2 });
            Assert.Equal(2, (await Json(await Http.GetAsync($"queues/{queueName}"))).GetProperty("settings").GetProperty("maxDepth").GetInt32());

            await Publish(queueName, new { body = 1 });
            await Publish(queueName, new { body = 2 });

            var full = await Http.PostAsJsonAsync($"queues/{queueName}/messages", new { body = 3 });
            await AssertProblem(full, HttpStatusCode.TooManyRequests, "queue_full");
            Assert.NotNull(full.Headers.RetryAfter);

            var raised = await Json(await Http.PatchAsJsonAsync($"queues/{queueName}", new { maxDepth = 3 }));
            Assert.Equal(3, raised.GetProperty("settings").GetProperty("maxDepth").GetInt32());

            var batch = await Publish(queueName, new { messages = new[] { new { body = 3 }, new { body = 4 } } });
            Assert.Equal("accepted", batch[0].GetProperty("status").GetString());
            Assert.Equal("rejected", batch[1].GetProperty("status").GetString());
            Assert.Equal("queue_full", batch[1].GetProperty("error").GetProperty("code").GetString());
            Assert.Equal(429, batch[1].GetProperty("error").GetProperty("status").GetInt32());

            //An idempotency key is not used up by a publish that was refused.
            await AssertProblem(await Http.PostAsJsonAsync($"queues/{queueName}/messages", new { idempotencyKey = "k", body = 5 }), HttpStatusCode.TooManyRequests, "queue_full");
            var removed = await Json(await Http.PatchAsJsonAsync($"queues/{queueName}", new { maxDepth = (int?)null }));
            Assert.Equal(JsonValueKind.Null, removed.GetProperty("settings").GetProperty("maxDepth").ValueKind);
            Assert.Equal("accepted", (await Publish(queueName, new { idempotencyKey = "k", body = 5 }))[0].GetProperty("status").GetString());
            Assert.Equal(4, (await Stats(queueName)).GetProperty("depth").GetInt32());

            await AssertProblem(await Http.PatchAsJsonAsync($"queues/{queueName}", new { maxDepth = -1 }), HttpStatusCode.BadRequest, "invalid_request");
        }

        #region Inspecting messages.

        [Theory(DisplayName = "HTTP: messages can be listed, read and deleted without consuming them.")]
        [InlineData("ephemeral")]
        [InlineData("persistent")]
        public async Task TestInspectMessages(string persistence)
        {
            var queueName = await CreateQueue(new { persistence });
            await Publish(queueName, new { messages = Enumerable.Range(0, 5).Select(i => new { body = new { n = i } }).ToArray() });

            var serials = new List<string>();
            string? cursor = null;
            do
            {
                var page = await Json(await Http.GetAsync($"queues/{queueName}/messages?limit=2&includeBody=true" + (cursor == null ? "" : $"&cursor={cursor}")));
                foreach (var message in page.GetProperty("messages").EnumerateArray())
                {
                    Assert.Equal(serials.Count, message.GetProperty("body").GetProperty("n").GetInt32());
                    serials.Add(message.GetProperty("serialNumber").GetString()!);
                }
                cursor = page.GetProperty("nextCursor").GetString();
            } while (cursor != null);

            Assert.Equal(5, serials.Count);

            var withoutBody = await Json(await Http.GetAsync($"queues/{queueName}/messages?limit=1"));
            Assert.False(withoutBody.GetProperty("messages")[0].TryGetProperty("body", out _));

            var single = await Json(await Http.GetAsync($"queues/{queueName}/messages/{serials[2]}"));
            Assert.Equal(2, single.GetProperty("body").GetProperty("n").GetInt32());

            Assert.Equal(HttpStatusCode.NoContent, (await Http.DeleteAsync($"queues/{queueName}/messages/{serials[2]}")).StatusCode);
            await AssertProblem(await Http.GetAsync($"queues/{queueName}/messages/{serials[2]}"), HttpStatusCode.NotFound, "message_not_found");
            await AssertProblem(await Http.DeleteAsync($"queues/{queueName}/messages/{serials[2]}"), HttpStatusCode.NotFound, "message_not_found");
            await AssertProblem(await Http.GetAsync($"queues/{queueName}/messages/abc"), HttpStatusCode.BadRequest, "invalid_request");
            Assert.Equal(4, (await Stats(queueName)).GetProperty("depth").GetInt32());

            Assert.Equal(HttpStatusCode.NoContent, (await Http.DeleteAsync($"queues/{queueName}/messages")).StatusCode);
            Assert.Equal(0, (await Stats(queueName)).GetProperty("depth").GetInt32());
        }

        [Fact(DisplayName = "HTTP: a message that is out for delivery cannot be deleted.")]
        public async Task TestDeleteMessageInDelivery()
        {
            var queueName = await CreateQueue();
            await Publish(queueName, new { body = 1 });
            var leased = (await Receive(queueName)).Single();

            await AssertProblem(await Http.DeleteAsync($"queues/{queueName}/messages/{leased.GetProperty("serialNumber").GetString()}"),
                HttpStatusCode.Conflict, "message_in_delivery");

            await Ack(leased);
        }

        #endregion

        #region Leases.

        [Fact(DisplayName = "HTTP: receive waits for a message, and returns empty when none arrives.")]
        public async Task TestLongPolling()
        {
            var queueName = await CreateQueue();
            Assert.Empty(await Receive(queueName, waitSeconds: 1));

            var pending = Receive(queueName, waitSeconds: 30);
            await Task.Delay(200);
            Assert.False(pending.IsCompleted);

            await Publish(queueName, new { body = 1 });
            await Ack((await pending).Single());
            Assert.True(await WaitForDepth(queueName, 0));
        }

        [Fact(DisplayName = "HTTP: acknowledgments report expired and unknown leases, and can be repeated.")]
        public async Task TestAcknowledgments()
        {
            var queueName = await CreateQueue();
            await Publish(queueName, new { body = 1 });

            var first = (await Receive(queueName, leaseSeconds: 1)).Single();
            var second = (await Receive(queueName, waitSeconds: 10)).Single();
            Assert.Equal(2, second.GetProperty("deliveryAttempt").GetInt32());
            Assert.Equal(first.GetProperty("messageId").GetString(), second.GetProperty("messageId").GetString());

            var leaseId = first.GetProperty("leaseId").GetString();
            await AssertProblem(await Http.PostAsJsonAsync($"leases/{leaseId}/ack", new { disposition = "consumed" }), HttpStatusCode.Conflict, "lease_expired");
            await AssertProblem(await Http.PostAsJsonAsync($"leases/{leaseId}/extend", new { leaseSeconds = 10 }), HttpStatusCode.Conflict, "lease_expired");
            await AssertProblem(await Http.PostAsJsonAsync("leases/nope/ack", new { disposition = "consumed" }), HttpStatusCode.NotFound, "lease_not_found");
            await AssertProblem(await Http.PostAsJsonAsync("leases/nope/extend", new { leaseSeconds = 10 }), HttpStatusCode.NotFound, "lease_not_found");

            await Ack(second);
            await Ack(second);
            Assert.True(await WaitForDepth(queueName, 0));
        }

        [Fact(DisplayName = "HTTP: a lease can be extended, and a message can be deferred or dead-lettered.")]
        public async Task TestExtendAndDispositions()
        {
            var queueName = await CreateQueue(new { deadLetter = new { } });
            await Publish(queueName, new { body = 1 });

            var leased = (await Receive(queueName, leaseSeconds: 1)).Single();
            var leaseId = leased.GetProperty("leaseId").GetString();

            var extended = await Http.PostAsJsonAsync($"leases/{leaseId}/extend", new { leaseSeconds = 30 });
            Assert.Equal(HttpStatusCode.OK, extended.StatusCode);
            Assert.True((await Json(extended)).GetProperty("leaseExpiresAt").GetDateTime() > leased.GetProperty("leaseExpiresAt").GetDateTime());
            await Task.Delay(2500);

            await AssertProblem(await Http.PostAsJsonAsync($"leases/{leaseId}/ack", new { disposition = "defer" }), HttpStatusCode.BadRequest, "invalid_request");
            await AssertProblem(await Http.PostAsJsonAsync($"leases/{leaseId}/ack", new { disposition = "maybe" }), HttpStatusCode.BadRequest, "invalid_request");

            var deferred = await Http.PostAsJsonAsync($"leases/{leaseId}/ack", new { disposition = "defer", deferSeconds = 1 });
            Assert.Equal(HttpStatusCode.NoContent, deferred.StatusCode);

            await Ack((await Receive(queueName, waitSeconds: 10)).Single(), "deadLetter");
            Assert.True(await WaitForDepth($"{queueName}.dlq", 1));
            Assert.Equal(0, (await Stats(queueName)).GetProperty("depth").GetInt32());
        }

        [Fact(DisplayName = "HTTP: a batch of acknowledgments reports each lease.")]
        public async Task TestBatchAck()
        {
            var queueName = await CreateQueue();
            await Publish(queueName, new { messages = Enumerable.Range(0, 3).Select(i => new { body = i }).ToArray() });

            var leased = await ReceiveAtLeast(queueName, 3, max: 3);
            Assert.Equal(3, leased.Length);

            var response = await Http.PostAsJsonAsync("leases/ack", new
            {
                acks = leased.Select(o => new { leaseId = o.GetProperty("leaseId").GetString(), disposition = "consumed" })
                    .Append(new { leaseId = (string?)"nope", disposition = "consumed" }).ToArray()
            });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var statuses = (await Json(response)).GetProperty("results").EnumerateArray().Select(o => o.GetProperty("status").GetString()).ToArray();
            Assert.Equal(["acked", "acked", "acked", "notFound"], statuses);
            Assert.True(await WaitForDepth(queueName, 0));

            //A mistake in a batch applies none of it.
            await Publish(queueName, new { body = 1 });
            var message = (await Receive(queueName)).Single();
            await AssertProblem(await Http.PostAsJsonAsync("leases/ack", new
            {
                acks = new[]
                {
                    new { leaseId = message.GetProperty("leaseId").GetString(), disposition = "consumed" },
                    new { leaseId = (string?)"other", disposition = "maybe" }
                }
            }), HttpStatusCode.BadRequest, "invalid_request");
            await Ack(message);
        }

        [Fact(DisplayName = "HTTP: a client that disconnects while waiting does not lose a message.")]
        public async Task TestClientDisconnect()
        {
            var queueName = await CreateQueue();

            using var cancel = new CancellationTokenSource();
            var abandoned = Http.PostAsJsonAsync($"queues/{queueName}/messages/receive", new { consumer = "worker", waitSeconds = 30 }, cancel.Token);
            await Task.Delay(300);
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);
            await Task.Delay(300);

            await Publish(queueName, new { body = 1 });
            var leased = (await Receive(queueName)).Single();
            Assert.Equal(1, leased.GetProperty("deliveryAttempt").GetInt32());
            await Ack(leased);
        }

        [Fact(DisplayName = "HTTP: the outstanding limit applies to leases.")]
        public async Task TestOutstandingLimit()
        {
            var queueName = await CreateQueue(new { maxOutstandingDeliveries = 2 });
            await Publish(queueName, new { messages = Enumerable.Range(0, 5).Select(i => new { body = i }).ToArray() });

            var leased = await ReceiveAtLeast(queueName, 2, max: 5);
            Assert.Equal(2, leased.Length);
            Assert.Equal(2, (await Stats(queueName)).GetProperty("outstandingDeliveries").GetInt32());
            Assert.Empty(await Receive(queueName, max: 5, waitSeconds: 1)); //Both slots are taken.

            foreach (var message in leased)
            {
                await Ack(message);
            }
        }

        #endregion

        #region The web UI, accounts and system.

        /// <summary>
        /// Signs in to the web UI the way a browser does, and returns a client that carries the session cookie.
        /// </summary>
        private async Task<HttpClient> SignInToWebUi()
        {
            var browser = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer() }) { BaseAddress = new Uri(service.WebBaseUrl) };
            var loginPage = await browser.GetStringAsync("/Login");
            var token = System.Text.RegularExpressions.Regex.Match(loginPage, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            var login = await browser.PostAsync("/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Username"] = ServiceFixture.UiUsername,
                ["Password"] = ServiceFixture.UiPassword,
                ["__RequestVerificationToken"] = token
            }));
            Assert.Equal("/", login.RequestMessage?.RequestUri?.AbsolutePath); //Signing in redirects to the home page.
            return browser;
        }

        [Fact(DisplayName = "HTTP: the web UI uses the API with its login session, but only with its header.")]
        public async Task TestWebUiSession()
        {
            var queueName = await CreateQueue();
            using var browser = await SignInToWebUi();

            //Without the header, the cookie alone is refused, which keeps other sites from using a signed-in browser.
            await AssertProblem(await browser.GetAsync($"/api/v1/queues/{queueName}"), HttpStatusCode.Unauthorized, "unauthorized");

            browser.DefaultRequestHeaders.Add("x-catmq-ui", "1");
            Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync($"/api/v1/queues/{queueName}")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await browser.DeleteAsync($"/api/v1/queues/{queueName}/messages")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await browser.DeleteAsync($"/api/v1/queues/{queueName}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Http.GetAsync($"queues/{queueName}")).StatusCode);

            //The header without a session is not enough either.
            using var anonymous = new HttpClient { BaseAddress = new Uri(service.WebBaseUrl) };
            anonymous.DefaultRequestHeaders.Add("x-catmq-ui", "1");
            await AssertProblem(await anonymous.GetAsync("/api/v1/queues"), HttpStatusCode.Unauthorized, "unauthorized");
        }

        [Fact(DisplayName = "HTTP: API keys and accounts can be deleted, and a deleted key stops working.")]
        public async Task TestDeleteApiKeyAndAccount()
        {
            using var disposable = new HttpClient { BaseAddress = Http.BaseAddress };
            disposable.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ServiceFixture.DisposableApiKey);
            Assert.Equal(HttpStatusCode.OK, (await disposable.GetAsync("queues")).StatusCode);

            var accountId = ServiceFixture.DisposableAccountId;
            Assert.Equal(HttpStatusCode.NoContent, (await Http.DeleteAsync($"accounts/{accountId}/api-keys/{ServiceFixture.DisposableApiKeyId}")).StatusCode);
            await AssertProblem(await disposable.GetAsync("queues"), HttpStatusCode.Unauthorized, "unauthorized");
            await AssertProblem(await Http.DeleteAsync($"accounts/{accountId}/api-keys/{ServiceFixture.DisposableApiKeyId}"), HttpStatusCode.NotFound, "api_key_not_found");

            Assert.Equal(HttpStatusCode.NoContent, (await Http.DeleteAsync($"accounts/{accountId}")).StatusCode);
            await AssertProblem(await Http.DeleteAsync($"accounts/{accountId}"), HttpStatusCode.NotFound, "account_not_found");
            await AssertProblem(await Http.DeleteAsync($"accounts/{accountId}/api-keys/{Guid.NewGuid()}"), HttpStatusCode.NotFound, "account_not_found");
        }

        [Fact(DisplayName = "HTTP: a garbage collection can be requested.")]
        public async Task TestGarbageCollection()
        {
            Assert.Equal(HttpStatusCode.NoContent, (await Http.PostAsync("system/gc", null)).StatusCode);
        }

        [Fact(DisplayName = "HTTP: a single connection can be removed by its subscriber ID, as the web UI does.")]
        public async Task TestRemoveConnectionBySubscriberId()
        {
            var queueName = await CreateQueue(new { consumptionScheme = "firstConsumer" });

            using var client = new CMqClient();
            client.Connect(Constants.HOST_NAME, service.QueuePort);
            int received = 0;
            client.Subscribe(queueName, (_, _) => { Interlocked.Increment(ref received); return CMqConsumeResult.Consumed(); });

            var consumers = (await Json(await Http.GetAsync($"queues/{queueName}/consumers"))).GetProperty("consumers");
            var tcp = Assert.Single(consumers.EnumerateArray());
            Assert.Equal("tcp", tcp.GetProperty("kind").GetString());

            //An unnamed connection is listed under its subscriber ID.
            Assert.Equal(HttpStatusCode.NoContent, (await Http.DeleteAsync($"queues/{queueName}/consumers/{tcp.GetProperty("name").GetString()}")).StatusCode);
            Assert.Empty((await Json(await Http.GetAsync($"queues/{queueName}/consumers"))).GetProperty("consumers").EnumerateArray());

            await Publish(queueName, new { body = 1 });
            await Task.Delay(300);
            Assert.Equal(0, received);
            Assert.Equal(1, (await Stats(queueName)).GetProperty("depth").GetInt32());
        }

        [Fact(DisplayName = "HTTP: removing a lease consumer by its subscriber ID removes it completely.")]
        public async Task TestRemoveLeaseConsumerBySubscriberId()
        {
            var queueName = await CreateQueue();
            Assert.Empty(await Receive(queueName, "worker", waitSeconds: 0));

            using var browser = await SignInToWebUi();
            browser.DefaultRequestHeaders.Add("x-catmq-ui", "1");

            //The web UI lists subscriptions by subscriber ID, and removes them that way.
            var page = await Json(await browser.GetAsync($"/queue/{queueName}?handler=QueueSubscribers&queueName={queueName}"));
            var subscriberId = Assert.Single(page.EnumerateArray()).GetProperty("subscriberId").GetString();
            Assert.Equal(HttpStatusCode.NoContent, (await browser.DeleteAsync($"/api/v1/queues/{queueName}/consumers/{subscriberId}")).StatusCode);

            Assert.Empty((await Json(await Http.GetAsync($"queues/{queueName}/consumers"))).GetProperty("consumers").EnumerateArray());

            //Receiving again registers it afresh, and it works.
            await Publish(queueName, new { body = 1 });
            await Ack((await Receive(queueName, "worker")).Single());
        }

        #endregion

        #region Consumers and dead letters.

        [Fact(DisplayName = "HTTP: consumers can be listed, and removing one stops the queue waiting for it.")]
        public async Task TestConsumers()
        {
            var queueName = await CreateQueue(new { consumptionScheme = "allConsumers" });
            Assert.Empty(await Receive(queueName, "billing", waitSeconds: 0));
            Assert.Empty(await Receive(queueName, "audit", waitSeconds: 0));

            var consumers = (await Json(await Http.GetAsync($"queues/{queueName}/consumers"))).GetProperty("consumers").EnumerateArray().ToArray();
            Assert.Equal(["audit", "billing"], consumers.Select(o => o.GetProperty("name").GetString()).ToArray());
            Assert.All(consumers, o => Assert.Equal("lease", o.GetProperty("kind").GetString()));

            await Publish(queueName, new { body = 1 });
            await Ack((await Receive(queueName, "billing")).Single());
            await Task.Delay(100);
            Assert.Equal(1, (await Stats(queueName)).GetProperty("depth").GetInt32());

            Assert.Equal(HttpStatusCode.NoContent, (await Http.DeleteAsync($"queues/{queueName}/consumers/audit")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Http.DeleteAsync($"queues/{queueName}/consumers/audit")).StatusCode);

            Assert.Empty(await Receive(queueName, "billing", waitSeconds: 1));
            Assert.True(await WaitForDepth(queueName, 0));
        }

        [Theory(DisplayName = "HTTP: dead letters can be moved back onto the queue.")]
        [InlineData("ephemeral")]
        [InlineData("persistent")]
        public async Task TestRedrive(string persistence)
        {
            var queueName = await CreateQueue(new { persistence, consumptionScheme = "firstConsumer", deadLetter = new { persistence } });
            await Publish(queueName, new { messageType = "orders.created", headers = new { tenant = "acme" }, body = new { n = 1 } });
            await Publish(queueName, new { body = new { n = 2 } });

            var first = (await Receive(queueName)).Single();
            await Ack(first, "deadLetter");
            await Ack((await Receive(queueName)).Single(), "deadLetter");
            Assert.True(await WaitForDepth($"{queueName}.dlq", 2));

            var partial = await Http.PostAsJsonAsync($"queues/{queueName}/redrive", new { max = 1 });
            Assert.Equal(1, (await Json(partial)).GetProperty("moved").GetInt32());
            Assert.Equal(1, (await Stats($"{queueName}.dlq")).GetProperty("depth").GetInt32());

            var redriven = (await Receive(queueName)).Single();
            Assert.Equal(first.GetProperty("messageId").GetString(), redriven.GetProperty("messageId").GetString());
            Assert.Equal("orders.created", redriven.GetProperty("messageType").GetString());
            Assert.Equal("acme", redriven.GetProperty("headers").GetProperty("tenant").GetString());
            Assert.Equal(1, redriven.GetProperty("deliveryAttempt").GetInt32());
            await Ack(redriven);

            var rest = await Http.PostAsync($"queues/{queueName}/redrive", JsonContent.Create(new { }));
            Assert.Equal(1, (await Json(rest)).GetProperty("moved").GetInt32());
            await Ack((await Receive(queueName)).Single());
            Assert.True(await WaitForDepth(queueName, 0));

            var noDeadLetters = await CreateQueue();
            await AssertProblem(await Http.PostAsync($"queues/{noDeadLetters}/redrive", JsonContent.Create(new { })), HttpStatusCode.NotFound, "queue_not_found");
        }

        #endregion

        #region Interoperability with the native protocol.

        [Fact(DisplayName = "HTTP: a publisher reaches a .NET client, and a .NET client reaches an HTTP consumer.")]
        public async Task TestInteroperability()
        {
            var queueName = await CreateQueue(new { consumptionScheme = "firstConsumer" });

            using var client = new CMqClient();
            client.Connect(Constants.HOST_NAME, service.QueuePort);

            //.NET publisher to HTTP consumer.
            client.Enqueue(queueName, new KeyValueMessage(5, "five"));
            var fromDotNet = (await Receive(queueName)).Single();
            Assert.Equal(5, fromDotNet.GetProperty("body").GetProperty("Key").GetInt32());
            Assert.Contains(nameof(KeyValueMessage), fromDotNet.GetProperty("messageType").GetString());
            await Ack(fromDotNet);

            //HTTP publisher to .NET consumer.
            var received = new ConcurrentBag<CMqReceivedMessage>();
            client.Subscribe(queueName, (_, m) => { received.Add(m); return CMqConsumeResult.Consumed(); });
            await Publish(queueName, new { messageType = "orders.created", body = new { Key = 7, Value = "seven" } });

            Assert.True(Wait.Until(() => received.Count == 1));
            var fromHttp = received.Single();
            Assert.Equal("orders.created", fromHttp.MessageType);
            Assert.Equal(7, fromHttp.Deserialize<KeyValueMessage>()?.Key);

            var consumers = (await Json(await Http.GetAsync($"queues/{queueName}/consumers"))).GetProperty("consumers");
            Assert.Contains(consumers.EnumerateArray(), o => o.GetProperty("kind").GetString() == "tcp");
        }

        #endregion
    }
}
