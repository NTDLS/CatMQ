using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NTDLS.CatMQ.Server;

namespace CatMQ.Service.Api
{
    /// <summary>
    /// Builds RFC 9457 problem details, which is how every error in the API is reported.
    /// </summary>
    internal static class Problems
    {
        public static ObjectResult Create(int status, string code, string title, string? detail = null)
        {
            var result = new ObjectResult(new ProblemDto { Status = status, Code = code, Title = title, Detail = detail })
            {
                StatusCode = status
            };
            result.ContentTypes.Add("application/problem+json");
            return result;
        }

        public static ObjectResult InvalidRequest(string title, string? detail = null)
            => Create(StatusCodes.Status400BadRequest, "invalid_request", title, string.IsNullOrWhiteSpace(detail) ? null : detail);

        public static ObjectResult NotFound(string code, string title)
            => Create(StatusCodes.Status404NotFound, code, title);

        public static ObjectResult LeaseExpired()
            => Create(StatusCodes.Status409Conflict, "lease_expired", "The lease has expired, so the message may be with another consumer.");
    }

    /// <summary>
    /// Translates exceptions from the queue server into problem details.
    /// </summary>
    internal class ApiExceptionFilterAttribute : ExceptionFilterAttribute
    {
        public override void OnException(ExceptionContext context)
        {
            context.ExceptionHandled = true;

            switch (context.Exception)
            {
                case CMqQueueNotFoundException ex:
                    context.Result = Problems.NotFound("queue_not_found", ex.Message);
                    break;
                case CMqQueuePersistenceChangeException ex:
                    context.Result = Problems.Create(StatusCodes.Status409Conflict, "queue_persistence_immutable", ex.Message);
                    break;
                case CMqQueueFullException ex:
                    context.HttpContext.Response.Headers.RetryAfter = "1";
                    context.Result = Problems.Create(StatusCodes.Status429TooManyRequests, "queue_full", ex.Message);
                    break;
                case CMqMessageInDeliveryException ex:
                    context.Result = Problems.Create(StatusCodes.Status409Conflict, "message_in_delivery", ex.Message);
                    break;
                case ArgumentException ex:
                    context.Result = Problems.InvalidRequest(ex.Message);
                    break;
                case OperationCanceledException when context.HttpContext.RequestAborted.IsCancellationRequested:
                    context.Result = new EmptyResult(); //The client went away, so nobody is listening.
                    break;
                case TimeoutException:
                    context.HttpContext.Response.Headers.RetryAfter = "1";
                    context.Result = Problems.Create(StatusCodes.Status503ServiceUnavailable, "service_busy", "The server is too busy to handle this request right now.");
                    break;
                default:
                    Serilog.Log.Error(context.Exception, "Unhandled error in the web API.");
                    context.Result = Problems.Create(StatusCodes.Status500InternalServerError, "internal_error", "An unexpected error occurred.");
                    break;
            }
        }
    }
}
