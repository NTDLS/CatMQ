using NTDLS.CatMQ.Shared;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NTDLS.CatMQ.Server.Server
{
    /// <summary>
    /// What is saved about a webhook, so that it survives a restart.
    /// </summary>
    internal class WebhookRegistration
    {
        public string Name { get; set; } = string.Empty;
        public CMqWebhookSettings Settings { get; set; } = new();
        public string Secret { get; set; } = string.Empty;

        /// <summary>
        /// The secret that was replaced, which deliveries are still signed with until PreviousSecretExpiresAt,
        /// so that receivers can move to the new one without rejecting deliveries in between.
        /// </summary>
        public string? PreviousSecret { get; set; }
        public DateTime? PreviousSecretExpiresAt { get; set; }

        public static string NewSecret()
            => "whsec_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    /// Delivers messages by POSTing them to a URL. The response status is the result: 2xx consumed, 410 drop, 422 dead-letter,
    /// and 429 or 503 defer (by Retry-After when it is given). Anything else, a timeout, or no response is a failed delivery.
    /// </summary>
    internal class WebhookDeliveryTransport : ICMqDeliveryTransport
    {
        private static readonly TimeSpan DEFAULT_RETRY_AFTER = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan MAX_RETRY_AFTER = TimeSpan.FromHours(1);

        //One client for every webhook, so that connections to the same receiver are reused. Redirects are not followed,
        //  because a delivery should not be sent somewhere other than where it was registered to go.
        private static readonly HttpClient _httpClient = new(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectTimeout = TimeSpan.FromSeconds(15)
        })
        {
            Timeout = Timeout.InfiniteTimeSpan //Each request has its own, from the webhook's settings.
        };

        private readonly object _statusLock = new();
        private readonly string _queueName;
        private int _inFlight;
        private volatile WebhookRegistration _registration;

        public WebhookDeliveryTransport(string queueName, WebhookRegistration registration)
        {
            _queueName = queueName;
            _registration = registration;
        }

        public Guid SubscriberId { get; } = Guid.NewGuid();
        public string Name => "webhook";

        public WebhookRegistration Registration
        {
            get => _registration;
            set => _registration = value;
        }

        public DateTime? LastSuccessAt { get; private set; }
        public DateTime? LastFailureAt { get; private set; }
        public string? LastFailure { get; private set; }
        public int ConsecutiveFailures { get; private set; }

        public bool IsReady
        {
            get
            {
                var settings = _registration.Settings;
                return settings.Enabled && (settings.MaxConcurrency == null || Volatile.Read(ref _inFlight) < settings.MaxConcurrency);
            }
        }

        public CMqWebhookDescriptor ToDescriptor()
        {
            lock (_statusLock)
            {
                return new CMqWebhookDescriptor
                {
                    Name = _registration.Name,
                    QueueName = _queueName,
                    Settings = _registration.Settings,
                    LastSuccessAt = LastSuccessAt,
                    LastFailureAt = LastFailureAt,
                    LastFailure = LastFailure,
                    ConsecutiveFailures = ConsecutiveFailures
                };
            }
        }

        private void RecordSuccess()
        {
            lock (_statusLock)
            {
                LastSuccessAt = DateTime.UtcNow;
                ConsecutiveFailures = 0;
            }
        }

        private void RecordFailure(string reason)
        {
            lock (_statusLock)
            {
                LastFailureAt = DateTime.UtcNow;
                LastFailure = reason;
                ConsecutiveFailures++;
            }
        }

        public async Task<CMqConsumeResult> DeliverAsync(CMqReceivedMessage message, CancellationToken cancellationToken)
        {
            var registration = _registration;
            var settings = registration.Settings;
            Interlocked.Increment(ref _inFlight);

            try
            {
                var deliveryId = Guid.NewGuid().ToString("N");
                var body = CreateBody(message, deliveryId);

                using var request = new HttpRequestMessage(HttpMethod.Post, settings.Url)
                {
                    Content = new ByteArrayContent(body)
                };
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

                foreach (var header in settings.Headers ?? [])
                {
                    if (request.Headers.TryAddWithoutValidation(header.Key, header.Value) == false)
                    {
                        request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }

                request.Headers.TryAddWithoutValidation("User-Agent", "CatMQ-Webhook/1");
                request.Headers.TryAddWithoutValidation("X-CatMQ-Signature", Sign(registration, body));
                request.Headers.TryAddWithoutValidation("X-CatMQ-Delivery-Id", deliveryId);
                request.Headers.TryAddWithoutValidation("X-CatMQ-Message-Id", message.MessageId.ToString());

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

                HttpResponseMessage response;
                try
                {
                    response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    //The queue is shutting down, which is not the receiver's fault.
                    throw new CMqDeliveryNotAcceptedException("The queue is shutting down.");
                }
                catch (OperationCanceledException)
                {
                    var reason = $"No response within {settings.TimeoutSeconds} seconds.";
                    RecordFailure(reason);
                    throw new TimeoutException(reason);
                }
                catch (HttpRequestException ex)
                {
                    RecordFailure(ex.Message);
                    throw;
                }

                using (response)
                {
                    var status = (int)response.StatusCode;

                    if (status is >= 200 and <= 299)
                    {
                        RecordSuccess();
                        return CMqConsumeResult.Consumed();
                    }
                    if (response.StatusCode == HttpStatusCode.Gone)
                    {
                        RecordSuccess();
                        return CMqConsumeResult.Drop();
                    }
                    if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
                    {
                        RecordSuccess();
                        return CMqConsumeResult.DeadLetter();
                    }
                    if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
                    {
                        RecordSuccess();
                        return CMqConsumeResult.Defer(RetryAfter(response));
                    }

                    var failure = $"HTTP {status} {response.ReasonPhrase}".Trim();
                    RecordFailure(failure);
                    throw new HttpRequestException($"The webhook responded with {failure}.");
                }
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        private static TimeSpan RetryAfter(HttpResponseMessage response)
        {
            var retryAfter = response.Headers.RetryAfter;
            var delay = retryAfter?.Delta ?? (retryAfter?.Date - DateTimeOffset.UtcNow) ?? DEFAULT_RETRY_AFTER;

            if (delay < TimeSpan.FromSeconds(1)) return TimeSpan.FromSeconds(1);
            return delay > MAX_RETRY_AFTER ? MAX_RETRY_AFTER : delay;
        }

        /// <summary>
        /// The signature header: a timestamp, and an HMAC-SHA256 of "timestamp.body" with each current secret. A receiver checks
        /// that one of the signatures matches its secret, and rejects deliveries with an old timestamp to stop them being replayed.
        /// </summary>
        internal static string Sign(WebhookRegistration registration, byte[] body, long? unixSeconds = null)
        {
            var timestamp = (unixSeconds ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToString(CultureInfo.InvariantCulture);

            string Signature(string secret)
            {
                using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
                hmac.TransformBlock(Encoding.UTF8.GetBytes(timestamp + "."), 0, timestamp.Length + 1, null, 0);
                hmac.TransformFinalBlock(body, 0, body.Length);
                return Convert.ToHexStringLower(hmac.Hash!);
            }

            var header = $"t={timestamp},v1={Signature(registration.Secret)}";

            if (registration.PreviousSecret != null && registration.PreviousSecretExpiresAt > DateTime.UtcNow)
            {
                header += $",v1={Signature(registration.PreviousSecret)}";
            }

            return header;
        }

        private static bool IsJson(string contentType)
        {
            var mediaType = contentType.Split(';')[0].Trim();
            return mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
                || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The delivery, in the same form as a message from the HTTP API, plus the queue name and a delivery ID.
        /// </summary>
        private byte[] CreateBody(CMqReceivedMessage message, string deliveryId)
        {
            using var stream = new MemoryStream();
            using (var json = new Utf8JsonWriter(stream))
            {
                json.WriteStartObject();
                json.WriteString("queue", _queueName);
                json.WriteString("deliveryId", deliveryId);
                json.WriteString("messageId", message.MessageId);
                json.WriteString("serialNumber", message.SerialNumber.ToString(CultureInfo.InvariantCulture));
                if (message.MessageType == null) json.WriteNull("messageType"); else json.WriteString("messageType", message.MessageType);
                json.WriteString("contentType", message.ContentType);

                if (message.BodyIsBase64)
                {
                    json.WriteString("bodyBase64", message.Body);
                }
                else if (IsJson(message.ContentType) && IsValidJson(message.Body))
                {
                    json.WritePropertyName("body");
                    json.WriteRawValue(message.Body, skipInputValidation: true);
                }
                else
                {
                    json.WriteString("body", message.Body);
                }

                if (message.Headers != null)
                {
                    json.WriteStartObject("headers");
                    foreach (var header in message.Headers)
                    {
                        json.WriteString(header.Key, header.Value);
                    }
                    json.WriteEndObject();
                }

                json.WriteString("enqueuedAt", DateTime.SpecifyKind(message.Timestamp, DateTimeKind.Utc));
                json.WriteNumber("deliveryAttempt", message.DeliveryAttempt);
                json.WriteEndObject();
            }
            return stream.ToArray();
        }

        private static bool IsValidJson(string text)
        {
            try
            {
                using var _ = JsonDocument.Parse(text);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
