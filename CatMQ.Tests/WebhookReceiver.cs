using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CatMQ.Tests
{
    /// <summary>
    /// A local HTTP server that webhooks deliver to in the tests. It records each request and answers as the test says.
    /// </summary>
    public sealed class WebhookReceiver : IDisposable
    {
        public record Delivery(string Path, string Body, NameValueCollection Headers, DateTime ReceivedAt)
        {
            public JsonElement Json => JsonDocument.Parse(Body).RootElement;
        }

        public record Response(int Status, int? RetryAfterSeconds = null, int DelayMilliseconds = 0, string? Location = null);

        private readonly HttpListener _listener = new();
        private int _concurrent;
        private int _maxConcurrent;

        public string Url { get; }
        public ConcurrentQueue<Delivery> Deliveries { get; } = new();
        public Func<Delivery, Response> Respond { get; set; } = _ => new Response(200);
        public int MaxConcurrent => Volatile.Read(ref _maxConcurrent);

        public WebhookReceiver()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            Url = $"http://localhost:{port}/";
            _listener.Prefixes.Add(Url);
            _listener.Start();
            _ = Task.Run(AcceptAsync);
        }

        private async Task AcceptAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch
                {
                    return; //Stopped.
                }
                _ = Task.Run(() => HandleAsync(context));
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            int concurrent = Interlocked.Increment(ref _concurrent);
            int current;
            while (concurrent > (current = Volatile.Read(ref _maxConcurrent)) && Interlocked.CompareExchange(ref _maxConcurrent, concurrent, current) != current)
            {
            }

            try
            {
                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                var delivery = new Delivery(context.Request.Url!.AbsolutePath, await reader.ReadToEndAsync(), context.Request.Headers, DateTime.UtcNow);
                var response = Respond(delivery);
                Deliveries.Enqueue(delivery);

                if (response.DelayMilliseconds > 0)
                {
                    await Task.Delay(response.DelayMilliseconds);
                }

                context.Response.StatusCode = response.Status;
                if (response.RetryAfterSeconds != null)
                {
                    context.Response.AddHeader("Retry-After", response.RetryAfterSeconds.Value.ToString());
                }
                if (response.Location != null)
                {
                    context.Response.RedirectLocation = response.Location;
                }
                context.Response.Close();
            }
            catch
            {
                //The sender gave up, such as after a timeout.
            }
            finally
            {
                Interlocked.Decrement(ref _concurrent);
            }
        }

        /// <summary>
        /// Checks a delivery's signature the way a receiver would: HMAC-SHA256 of "timestamp.body" with the secret.
        /// </summary>
        public static bool IsSignedWith(Delivery delivery, string secret)
        {
            var parts = delivery.Headers["X-CatMQ-Signature"]!.Split(',');
            var timestamp = parts.Single(o => o.StartsWith("t="))[2..];
            var expected = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{delivery.Body}")));
            return parts.Where(o => o.StartsWith("v1=")).Any(o => o[3..] == expected);
        }

        public void Dispose()
        {
            _listener.Stop();
            _listener.Close();
        }
    }
}
