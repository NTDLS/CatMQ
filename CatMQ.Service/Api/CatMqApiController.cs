using Microsoft.AspNetCore.Mvc;
using NTDLS.CatMQ.Server;
using NTDLS.CatMQ.Shared;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CatMQ.Service.Api
{
    /// <summary>
    /// The language-neutral HTTP API. The contract is docs/api/catmq.openapi.yaml.
    /// </summary>
    [ApiController]
    [Route("api/v1")]
    [ApiExceptionFilter]
    public partial class CatMqApiController(CMqServer mqServer, IdempotencyStore idempotencyStore)
        : ControllerBase
    {
        private const int MAX_BATCH_SIZE = 1000;
        private const int MAX_RECEIVE_WAIT_SECONDS = 60;
        private const int MAX_LEASE_SECONDS = 43200;

        [GeneratedRegex("^[A-Za-z0-9._-]{1,128}$")]
        private static partial Regex ConsumerNameRegex();

        private static void ValidateLimit(int limit)
        {
            if (limit is < 1 or > MAX_BATCH_SIZE)
            {
                throw new ArgumentException($"limit must be between 1 and {MAX_BATCH_SIZE}.");
            }
        }

        private QueueDto GetQueueDto(string queueName)
        {
            var queue = mqServer.GetQueue(queueName) ?? throw new CMqQueueNotFoundException(queueName);
            return Mapping.ToQueue(queue, mqServer.GetQueueConfiguration(queueName));
        }

        #region System.

        [HttpGet("health")]
        public IActionResult GetHealth()
            => Ok(new { status = "ok", version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0" });

        #endregion

        #region Queues.

        [HttpGet("queues")]
        public IActionResult ListQueues([FromQuery] int limit = 100, [FromQuery] string? cursor = null)
        {
            ValidateLimit(limit);

            var page = (mqServer.GetQueues() ?? [])
                .OrderBy(o => o.QueueName, StringComparer.OrdinalIgnoreCase)
                .Where(o => cursor == null || StringComparer.OrdinalIgnoreCase.Compare(o.QueueName, cursor) > 0)
                .Take(limit + 1)
                .ToList();

            bool hasMore = page.Count > limit;
            var queues = page.Take(limit).Select(o => Mapping.ToQueue(o, mqServer.GetQueueConfiguration(o.QueueName))).ToList();

            return Ok(new { queues, nextCursor = hasMore ? queues[^1].Name : null });
        }

        [HttpPut("queues/{queue}")]
        public IActionResult CreateQueue(string queue, [FromBody] QueueSettingsDto settings)
        {
            bool alreadyExists = mqServer.GetQueue(queue) != null;

            mqServer.CreateQueue(Mapping.ToConfiguration(queue, settings));

            var result = GetQueueDto(queue);
            return alreadyExists ? Ok(result) : StatusCode(StatusCodes.Status201Created, result);
        }

        [HttpGet("queues/{queue}")]
        public IActionResult GetQueue(string queue)
            => Ok(GetQueueDto(queue));

        [HttpPatch("queues/{queue}")]
        public IActionResult UpdateQueue(string queue, [FromBody] QueueSettingsUpdateDto update)
        {
            var configuration = mqServer.GetQueueConfiguration(queue);
            Mapping.ApplyUpdate(configuration, update);
            mqServer.CreateQueue(configuration);
            return Ok(GetQueueDto(queue));
        }

        [HttpDelete("queues/{queue}")]
        public IActionResult DeleteQueue(string queue)
        {
            mqServer.DeleteQueue(queue);
            return NoContent();
        }

        [HttpPost("queues/{queue}/redrive")]
        public IActionResult RedriveDeadLetters(string queue, [FromBody] RedriveRequestDto? request = null)
        {
            if (request?.Max < 1)
            {
                throw new ArgumentException("max must be at least 1.");
            }
            return Ok(new { moved = mqServer.RedriveDeadLetters(queue, request?.Max) });
        }

        #endregion

        #region Messages.

        /// <summary>
        /// Reads one message from a publish request. Throws ArgumentException if it is not valid.
        /// </summary>
        private static (string? MessageType, string Body, string ContentType, bool IsBase64, Dictionary<string, string>? Headers, TimeSpan? Defer, string? IdempotencyKey)
            ParsePublish(JsonElement item)
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("A message must be a JSON object.");
            }

            string? ReadString(string name)
            {
                if (item.TryGetProperty(name, out var value) == false || value.ValueKind == JsonValueKind.Null)
                {
                    return null;
                }
                return value.ValueKind == JsonValueKind.String ? value.GetString() : throw new ArgumentException($"{name} must be a string.");
            }

            var contentType = ReadString("contentType") ?? CMqDefaults.DEFAULT_CONTENT_TYPE;
            var bodyBase64 = ReadString("bodyBase64");
            bool hasBody = item.TryGetProperty("body", out var body);

            if (hasBody == (bodyBase64 != null))
            {
                throw new ArgumentException("A message must have exactly one of body or bodyBase64.");
            }

            string storedBody;
            if (bodyBase64 != null)
            {
                try
                {
                    Convert.FromBase64String(bodyBase64);
                }
                catch (FormatException)
                {
                    throw new ArgumentException("bodyBase64 is not valid base64.");
                }
                storedBody = bodyBase64;
            }
            else if (Mapping.IsJson(contentType))
            {
                storedBody = body.GetRawText();
            }
            else
            {
                storedBody = body.ValueKind == JsonValueKind.String
                    ? body.GetString()!
                    : throw new ArgumentException("body must be a string unless contentType is JSON.");
            }

            if (storedBody.Length == 0)
            {
                throw new ArgumentException("The message body cannot be empty.");
            }

            Dictionary<string, string>? headers = null;
            if (item.TryGetProperty("headers", out var headersElement) && headersElement.ValueKind != JsonValueKind.Null)
            {
                try
                {
                    headers = headersElement.Deserialize<Dictionary<string, string>>();
                }
                catch (JsonException)
                {
                    throw new ArgumentException("headers must be an object of strings.");
                }
            }

            TimeSpan? defer = null;
            if (item.TryGetProperty("deferSeconds", out var deferElement) && deferElement.ValueKind != JsonValueKind.Null)
            {
                if (deferElement.TryGetInt32(out var deferSeconds) == false || deferSeconds < 0)
                {
                    throw new ArgumentException("deferSeconds must be a whole number that is at least 0.");
                }
                defer = deferSeconds > 0 ? TimeSpan.FromSeconds(deferSeconds) : null;
            }

            return (ReadString("messageType"), storedBody, contentType, bodyBase64 != null, headers, defer, ReadString("idempotencyKey"));
        }

        private PublishItemResultDto PublishOne(string queue, JsonElement item)
        {
            var message = ParsePublish(item);

            Guid Publish() => mqServer.Enqueue(queue, message.Defer, message.MessageType, message.Body,
                message.ContentType, message.Headers, message.IsBase64);

            if (message.IdempotencyKey == null)
            {
                return new PublishItemResultDto { Status = "accepted", MessageId = Publish() };
            }

            var (messageId, isDuplicate) = idempotencyStore.Publish(queue, message.IdempotencyKey, Publish);
            return new PublishItemResultDto { Status = isDuplicate ? "duplicate" : "accepted", MessageId = messageId };
        }

        [HttpPost("queues/{queue}/messages")]
        public IActionResult PublishMessages(string queue, [FromBody] JsonElement request)
        {
            if (mqServer.GetQueue(queue) == null)
            {
                throw new CMqQueueNotFoundException(queue);
            }

            if (request.ValueKind == JsonValueKind.Object && request.TryGetProperty("messages", out var messages))
            {
                if (messages.ValueKind != JsonValueKind.Array || messages.GetArrayLength() is 0 or > MAX_BATCH_SIZE)
                {
                    throw new ArgumentException($"messages must be an array of 1 to {MAX_BATCH_SIZE} messages.");
                }

                var results = new List<PublishItemResultDto>();
                foreach (var item in messages.EnumerateArray())
                {
                    try
                    {
                        results.Add(PublishOne(queue, item));
                    }
                    catch (ArgumentException ex)
                    {
                        results.Add(new PublishItemResultDto
                        {
                            Status = "rejected",
                            Error = new ProblemDto { Status = StatusCodes.Status400BadRequest, Code = "invalid_request", Title = ex.Message }
                        });
                    }
                    catch (CMqQueueFullException ex)
                    {
                        results.Add(new PublishItemResultDto
                        {
                            Status = "rejected",
                            Error = new ProblemDto { Status = StatusCodes.Status429TooManyRequests, Code = "queue_full", Title = ex.Message }
                        });
                    }
                }
                return Accepted(new { results });
            }

            return Accepted(new { results = new[] { PublishOne(queue, request) } });
        }

        private static ulong? ParseSerialNumber(string? value, string name)
        {
            if (value == null)
            {
                return null;
            }
            return ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var serialNumber)
                ? serialNumber
                : throw new ArgumentException($"{name} is not valid.");
        }

        [HttpGet("queues/{queue}/messages")]
        public IActionResult ListMessages(string queue, [FromQuery] int limit = 100, [FromQuery] string? cursor = null, [FromQuery] bool includeBody = false)
        {
            ValidateLimit(limit);

            var page = mqServer.GetMessagesAfter(queue, ParseSerialNumber(cursor, "cursor"), limit + 1);
            bool hasMore = page.Count > limit;
            var messages = page.Take(limit).Select(o => Mapping.ToMessage(o, includeBody)).ToList();

            return Ok(new { messages, nextCursor = hasMore ? messages[^1].SerialNumber : null });
        }

        [HttpDelete("queues/{queue}/messages")]
        public IActionResult PurgeQueue(string queue)
        {
            if (mqServer.GetQueue(queue) == null)
            {
                throw new CMqQueueNotFoundException(queue);
            }
            mqServer.PurgeQueue(queue);
            return NoContent();
        }

        [HttpGet("queues/{queue}/messages/{serialNumber}")]
        public IActionResult GetMessage(string queue, string serialNumber)
        {
            var serial = ParseSerialNumber(serialNumber, "serialNumber")!.Value;
            var message = mqServer.GetMessagesAfter(queue, serial == 0 ? null : serial - 1, 1).FirstOrDefault();

            return message?.SerialNumber == serial
                ? Ok(Mapping.ToMessage(message, true))
                : Problems.NotFound("message_not_found", $"Message not found: [{serialNumber}].");
        }

        [HttpDelete("queues/{queue}/messages/{serialNumber}")]
        public IActionResult DeleteMessage(string queue, string serialNumber)
        {
            return mqServer.DeleteMessage(queue, ParseSerialNumber(serialNumber, "serialNumber")!.Value)
                ? NoContent()
                : Problems.NotFound("message_not_found", $"Message not found: [{serialNumber}].");
        }

        #endregion

        #region Leases.

        [HttpPost("queues/{queue}/messages/receive")]
        public async Task<IActionResult> ReceiveMessages(string queue, [FromBody] ReceiveRequestDto request)
        {
            if (request.Consumer == null || ConsumerNameRegex().IsMatch(request.Consumer) == false)
            {
                throw new ArgumentException("consumer is required, and may contain only letters, numbers, dots, underscores and hyphens.");
            }
            if (request.Max is < 1 or > MAX_BATCH_SIZE)
            {
                throw new ArgumentException($"max must be between 1 and {MAX_BATCH_SIZE}.");
            }
            if (request.WaitSeconds is < 0 or > MAX_RECEIVE_WAIT_SECONDS)
            {
                throw new ArgumentException($"waitSeconds must be between 0 and {MAX_RECEIVE_WAIT_SECONDS}.");
            }
            if (request.LeaseSeconds is < 1 or > MAX_LEASE_SECONDS)
            {
                throw new ArgumentException($"leaseSeconds must be between 1 and {MAX_LEASE_SECONDS}.");
            }

            var leased = await mqServer.ReceiveAsync(queue, request.Consumer, request.Max, TimeSpan.FromSeconds(request.WaitSeconds),
                TimeSpan.FromSeconds(request.LeaseSeconds), HttpContext.RequestAborted);

            return Ok(new { messages = leased.Select(Mapping.ToMessage) });
        }

        [HttpPost("leases/{leaseId}/ack")]
        public IActionResult AckLease(string leaseId, [FromBody] AckDto ack)
        {
            return mqServer.AcknowledgeLease(leaseId, Mapping.ParseAck(ack)) switch
            {
                CMqLeaseStatus.Acknowledged => NoContent(),
                CMqLeaseStatus.NotFound => Problems.NotFound("lease_not_found", $"Lease not found: [{leaseId}]."),
                _ => Problems.LeaseExpired()
            };
        }

        [HttpPost("leases/{leaseId}/extend")]
        public IActionResult ExtendLease(string leaseId, [FromBody] ExtendRequestDto request)
        {
            if (request.LeaseSeconds is < 1 or > MAX_LEASE_SECONDS)
            {
                throw new ArgumentException($"leaseSeconds must be between 1 and {MAX_LEASE_SECONDS}.");
            }

            return mqServer.ExtendLease(leaseId, TimeSpan.FromSeconds(request.LeaseSeconds), out var expiresAt) switch
            {
                CMqLeaseStatus.Active => Ok(new { leaseExpiresAt = expiresAt }),
                CMqLeaseStatus.NotFound => Problems.NotFound("lease_not_found", $"Lease not found: [{leaseId}]."),
                _ => Problems.LeaseExpired()
            };
        }

        [HttpPost("leases/ack")]
        public IActionResult AckLeases([FromBody] AckBatchDto request)
        {
            if (request.Acks == null || request.Acks.Count is 0 or > MAX_BATCH_SIZE)
            {
                throw new ArgumentException($"acks must contain 1 to {MAX_BATCH_SIZE} acknowledgments.");
            }
            if (request.Acks.Any(o => string.IsNullOrEmpty(o.LeaseId)))
            {
                throw new ArgumentException("Each acknowledgment needs a leaseId.");
            }

            //Validate them all first, so that a mistake does not leave a batch half applied.
            var acks = request.Acks.Select(o => (LeaseId: o.LeaseId!, Result: Mapping.ParseAck(o))).ToList();

            var results = acks.Select(o => new
            {
                leaseId = o.LeaseId,
                status = mqServer.AcknowledgeLease(o.LeaseId, o.Result) switch
                {
                    CMqLeaseStatus.Acknowledged => "acked",
                    CMqLeaseStatus.NotFound => "notFound",
                    _ => "leaseExpired"
                }
            }).ToList();

            return Ok(new { results });
        }

        #endregion

        #region Consumers.

        [HttpGet("queues/{queue}/consumers")]
        public IActionResult ListConsumers(string queue)
        {
            var consumers = (mqServer.GetSubscribers(queue) ?? [])
                .GroupBy(o => o.ConsumerId)
                .Select(o => new ConsumerDto
                {
                    Name = o.First().ConsumerName ?? o.First().SubscriberId.ToString(),
                    Kind = o.First().TransportName,
                    Connections = o.Count()
                })
                .OrderBy(o => o.Name, StringComparer.Ordinal);

            return Ok(new { consumers });
        }

        [HttpDelete("queues/{queue}/consumers/{consumer}")]
        public IActionResult DeleteConsumer(string queue, string consumer)
        {
            mqServer.RemoveLeaseConsumer(queue, consumer);
            return NoContent();
        }

        #endregion
    }
}
