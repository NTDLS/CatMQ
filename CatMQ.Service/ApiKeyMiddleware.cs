using System.Security.Cryptography;
using System.Text;

namespace CatMQ.Service
{
    public class ApiKeyMiddleware
    {
        private readonly RequestDelegate _next;
        private const string ApiKeyHeaderName = "x-catmq-api-Key";

        public ApiKeyMiddleware(RequestDelegate next)
        {
            _next = next;
        }
        private static bool ConstantTimeKeyEquals(string a, string b)
        {
            var aBytes = SHA256.HashData(Encoding.UTF8.GetBytes(a));
            var bBytes = SHA256.HashData(Encoding.UTF8.GetBytes(b));
            return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path;

            // Only enforce API key for the API (e.g. /api/)
            if (!path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            var accounts = Configs.GetAccounts();

            if (!context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var extractedApiKey))
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("API Key was not provided.");
                return;
            }

            var apiKeys = accounts.SelectMany(o => o.ApiKeys.Select(k => k.Key));

            string? extractedKey = extractedApiKey.ToString();
            bool authorized = apiKeys.Any(key => ConstantTimeKeyEquals(key, extractedKey));

            if (!authorized)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Unauthorized client.");
                return;
            }

            await _next(context);
        }
    }
}
