using System.Security.Cryptography;
using System.Text;

namespace CatMQ.Service
{
    public class ApiKeyMiddleware
    {
        private readonly RequestDelegate _next;
        private const string ApiKeyHeaderName = "x-catmq-api-Key";

        /// <summary>
        /// The web UI sends this header with its API requests. Another site cannot add a custom header to a request without
        /// the browser asking this server first, which it refuses, so this keeps other sites from using a signed-in browser's cookie.
        /// </summary>
        public const string UiHeaderName = "x-catmq-ui";

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

            // Only enforce API key for the API (e.g. /api/). The health check is for load balancers and monitors, which have no key.
            if (!path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/api/v1/health", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            //The web UI calls the API with the user's login session rather than an API key.
            if (context.User.Identity?.IsAuthenticated == true && context.Request.Headers.ContainsKey(UiHeaderName))
            {
                await _next(context);
                return;
            }

            var accounts = Configs.GetAccounts();

            string? extractedKey = context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var extractedApiKey) ? extractedApiKey.ToString() : null;

            if (extractedKey == null)
            {
                var authorization = context.Request.Headers.Authorization.ToString();
                if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    extractedKey = authorization["Bearer ".Length..].Trim();
                }
            }

            if (extractedKey == null)
            {
                await WriteUnauthorized(context, Localize("API Key was not provided."));
                return;
            }

            var apiKeys = accounts.SelectMany(o => o.ApiKeys.Select(k => k.Key));

            bool authorized = apiKeys.Any(key => ConstantTimeKeyEquals(key, extractedKey));

            if (!authorized)
            {
                await WriteUnauthorized(context, Localize("Unauthorized client."));
                return;
            }

            await _next(context);
        }

        private static async Task WriteUnauthorized(HttpContext context, string message)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { title = message, status = 401, code = "unauthorized" },
                options: null, contentType: "application/problem+json");
        }
    }
}
