using NTDLS.CatMQ.Server;
using NTDLS.CatMQ.Server.Management;
using NTDLS.CatMQ.Shared;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CatMQ.Service.Api
{
    public class ProblemDto
    {
        public string Title { get; set; } = string.Empty;
        public int Status { get; set; }
        public string Code { get; set; } = string.Empty;

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Detail { get; set; }
    }

    public class DeadLetterSettingsDto
    {
        public string? Persistence { get; set; }
        public int? MaxMessageAgeSeconds { get; set; }
    }

    public class QueueSettingsDto
    {
        public string? Persistence { get; set; }
        public string? ConsumptionScheme { get; set; }
        public string? DeliveryScheme { get; set; }
        public int? MaxOutstandingDeliveries { get; set; }
        public int? MaxDeliveryAttempts { get; set; }
        public int? MaxMessageAgeSeconds { get; set; }
        public int? DeliveryThrottleMilliseconds { get; set; }
        public int? MaxDepth { get; set; }
        public DeadLetterSettingsDto? DeadLetter { get; set; }
    }

    public class QueueSettingsUpdateDto
    {
        public int? MaxOutstandingDeliveries { get; set; }
        public int? MaxDeliveryAttempts { get; set; }

        /// <summary>
        /// A plain JsonElement, because it can distinguish an explicit null, which
        /// removes the limit, from a field that was not supplied (which is Undefined).
        /// </summary>
        public JsonElement MaxMessageAgeSeconds { get; set; }
        public int? DeliveryThrottleMilliseconds { get; set; }

        /// <summary>
        /// Null removes the limit, as does 0. Left out, the limit is unchanged.
        /// </summary>
        public JsonElement MaxDepth { get; set; }
    }

    public class QueueStatsDto
    {
        public int Depth { get; set; }
        public int OutstandingDeliveries { get; set; }
        public int ConsumerCount { get; set; }
        public string ReceivedCount { get; set; } = "0";
        public string DeliveredCount { get; set; } = "0";
        public string FailedCount { get; set; } = "0";
        public string DeferredCount { get; set; } = "0";
        public string ExpiredCount { get; set; } = "0";
        public string DroppedCount { get; set; } = "0";
        public string DeadLetteredCount { get; set; } = "0";
    }

    public class QueueDto
    {
        public string Name { get; set; } = string.Empty;
        public bool IsDeadLetter { get; set; }
        public QueueSettingsDto Settings { get; set; } = new();
        public QueueStatsDto Stats { get; set; } = new();
    }

    public class MessageDto
    {
        public Guid MessageId { get; set; }
        public string SerialNumber { get; set; } = string.Empty;
        public string? MessageType { get; set; }
        public string ContentType { get; set; } = string.Empty;

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Body { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? BodyBase64 { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string>? Headers { get; set; }

        public DateTime EnqueuedAt { get; set; }
        public DateTime? DeferredUntil { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? DeliveryAttempt { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LeaseId { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DateTime? LeaseExpiresAt { get; set; }
    }

    public class PublishItemResultDto
    {
        public string Status { get; set; } = string.Empty;

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Guid? MessageId { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ProblemDto? Error { get; set; }
    }

    public class ReceiveRequestDto
    {
        public string? Consumer { get; set; }
        public int Max { get; set; } = 1;
        public int WaitSeconds { get; set; } = 20;
        public int LeaseSeconds { get; set; } = 60;
    }

    public class AckDto
    {
        public string? LeaseId { get; set; }
        public string? Disposition { get; set; }
        public int? DeferSeconds { get; set; }
    }

    public class AckBatchDto
    {
        public List<AckDto>? Acks { get; set; }
    }

    public class ExtendRequestDto
    {
        public int LeaseSeconds { get; set; }
    }

    public class RedriveRequestDto
    {
        public int? Max { get; set; }
    }

    public class ConsumerDto
    {
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public int Connections { get; set; }
    }

    /// <summary>
    /// Converts between the API's language-neutral representation and the queue server's types.
    /// </summary>
    internal static class Mapping
    {
        public static CMqPersistenceScheme ParsePersistence(string? value) => value switch
        {
            null or "ephemeral" => CMqPersistenceScheme.Ephemeral,
            "persistent" => CMqPersistenceScheme.Persistent,
            _ => throw new ArgumentException($"Unknown persistence: [{value}].")
        };

        public static string Format(CMqPersistenceScheme value)
            => value == CMqPersistenceScheme.Persistent ? "persistent" : "ephemeral";

        public static CMqConsumptionScheme ParseConsumptionScheme(string? value) => value switch
        {
            null or "allConsumers" => CMqConsumptionScheme.AllSubscribersSatisfied,
            "firstConsumer" => CMqConsumptionScheme.FirstConsumedSubscriber,
            _ => throw new ArgumentException($"Unknown consumption scheme: [{value}].")
        };

        public static string Format(CMqConsumptionScheme value)
            => value == CMqConsumptionScheme.FirstConsumedSubscriber ? "firstConsumer" : "allConsumers";

        public static CMqDeliveryScheme ParseDeliveryScheme(string? value) => value switch
        {
            null or "random" => CMqDeliveryScheme.Random,
            "balanced" => CMqDeliveryScheme.Balanced,
            _ => throw new ArgumentException($"Unknown delivery scheme: [{value}].")
        };

        public static string Format(CMqDeliveryScheme value)
            => value == CMqDeliveryScheme.Balanced ? "balanced" : "random";

        public static CMqConsumeResult ParseAck(AckDto ack)
        {
            if (ack.Disposition == "defer")
            {
                if (ack.DeferSeconds is null or < 1)
                {
                    throw new ArgumentException("deferSeconds is required when the disposition is defer.");
                }
                return CMqConsumeResult.Defer(TimeSpan.FromSeconds(ack.DeferSeconds.Value));
            }

            return ack.Disposition switch
            {
                "consumed" => CMqConsumeResult.Consumed(),
                "notInterested" => CMqConsumeResult.NotInterested(),
                "notConsumed" => CMqConsumeResult.NotConsumed(),
                "deadLetter" => CMqConsumeResult.DeadLetter(),
                "drop" => CMqConsumeResult.Drop(),
                _ => throw new ArgumentException($"Unknown disposition: [{ack.Disposition}].")
            };
        }

        private static void RequireAtLeast(int? value, int minimum, string name)
        {
            if (value < minimum)
            {
                throw new ArgumentException($"{name} must be at least {minimum}.");
            }
        }

        /// <summary>
        /// Builds a full configuration. As with any PUT, whatever is left out returns to its default.
        /// </summary>
        public static CMqQueueConfiguration ToConfiguration(string queueName, QueueSettingsDto settings)
        {
            RequireAtLeast(settings.MaxOutstandingDeliveries, 1, "maxOutstandingDeliveries");
            RequireAtLeast(settings.MaxDeliveryAttempts, 0, "maxDeliveryAttempts");
            RequireAtLeast(settings.MaxMessageAgeSeconds, 0, "maxMessageAgeSeconds");
            RequireAtLeast(settings.DeliveryThrottleMilliseconds, 0, "deliveryThrottleMilliseconds");
            RequireAtLeast(settings.DeadLetter?.MaxMessageAgeSeconds, 0, "deadLetter.maxMessageAgeSeconds");
            RequireAtLeast(settings.MaxDepth, 0, "maxDepth");

            var configuration = new CMqQueueConfiguration(queueName)
            {
                PersistenceScheme = ParsePersistence(settings.Persistence),
                ConsumptionScheme = ParseConsumptionScheme(settings.ConsumptionScheme),
                DeliveryScheme = ParseDeliveryScheme(settings.DeliveryScheme)
            };

            configuration.MaxOutstandingDeliveries = settings.MaxOutstandingDeliveries ?? configuration.MaxOutstandingDeliveries;
            configuration.MaxDeliveryAttempts = settings.MaxDeliveryAttempts ?? configuration.MaxDeliveryAttempts;
            configuration.MaxMessageAge = ToAge(settings.MaxMessageAgeSeconds);
            configuration.DeliveryThrottle = TimeSpan.FromMilliseconds(settings.DeliveryThrottleMilliseconds ?? 0);
            configuration.MaxDepth = settings.MaxDepth is null or 0 ? null : settings.MaxDepth;

            if (settings.DeadLetter != null)
            {
                configuration.DeadLetterConfiguration = new CMqDeadLetterQueueConfiguration
                {
                    PersistenceScheme = ParsePersistence(settings.DeadLetter.Persistence),
                    MaxMessageAge = ToAge(settings.DeadLetter.MaxMessageAgeSeconds)
                };
            }

            return configuration;
        }

        private static TimeSpan? ToAge(int? seconds)
            => seconds is null or 0 ? null : TimeSpan.FromSeconds(seconds.Value);

        public static void ApplyUpdate(CMqQueueConfiguration configuration, QueueSettingsUpdateDto update)
        {
            RequireAtLeast(update.MaxOutstandingDeliveries, 1, "maxOutstandingDeliveries");
            RequireAtLeast(update.MaxDeliveryAttempts, 0, "maxDeliveryAttempts");
            RequireAtLeast(update.DeliveryThrottleMilliseconds, 0, "deliveryThrottleMilliseconds");

            configuration.MaxOutstandingDeliveries = update.MaxOutstandingDeliveries ?? configuration.MaxOutstandingDeliveries;
            configuration.MaxDeliveryAttempts = update.MaxDeliveryAttempts ?? configuration.MaxDeliveryAttempts;

            if (update.DeliveryThrottleMilliseconds != null)
            {
                configuration.DeliveryThrottle = TimeSpan.FromMilliseconds(update.DeliveryThrottleMilliseconds.Value);
            }

            var maxDepth = update.MaxDepth;
            if (maxDepth.ValueKind != JsonValueKind.Undefined)
            {
                if (maxDepth.ValueKind == JsonValueKind.Null)
                {
                    configuration.MaxDepth = null;
                }
                else if (maxDepth.TryGetInt32(out var depth) && depth >= 0)
                {
                    configuration.MaxDepth = depth == 0 ? null : depth;
                }
                else
                {
                    throw new ArgumentException("maxDepth must be null or at least 0.");
                }
            }

            var age = update.MaxMessageAgeSeconds;
            if (age.ValueKind != JsonValueKind.Undefined)
            {
                if (age.ValueKind == JsonValueKind.Null)
                {
                    configuration.MaxMessageAge = null;
                }
                else if (age.TryGetInt32(out var seconds) && seconds >= 0)
                {
                    configuration.MaxMessageAge = ToAge(seconds);
                }
                else
                {
                    throw new ArgumentException("maxMessageAgeSeconds must be null or at least 0.");
                }
            }
        }

        public static QueueDto ToQueue(CMqQueueDescriptor queue, CMqQueueConfiguration configuration)
        {
            return new QueueDto
            {
                Name = queue.QueueName,
                IsDeadLetter = configuration.IsDeadLetter,
                Settings = new QueueSettingsDto
                {
                    Persistence = Format(configuration.PersistenceScheme),
                    ConsumptionScheme = Format(configuration.ConsumptionScheme),
                    DeliveryScheme = Format(configuration.DeliveryScheme),
                    MaxOutstandingDeliveries = configuration.MaxOutstandingDeliveries,
                    MaxDeliveryAttempts = configuration.MaxDeliveryAttempts,
                    MaxMessageAgeSeconds = (int?)configuration.MaxMessageAge?.TotalSeconds,
                    DeliveryThrottleMilliseconds = (int)configuration.DeliveryThrottle.TotalMilliseconds,
                    MaxDepth = configuration.MaxDepth,
                    DeadLetter = configuration.DeadLetterConfiguration == null ? null : new DeadLetterSettingsDto
                    {
                        Persistence = Format(configuration.DeadLetterConfiguration.PersistenceScheme),
                        MaxMessageAgeSeconds = (int?)configuration.DeadLetterConfiguration.MaxMessageAge?.TotalSeconds
                    }
                },
                Stats = new QueueStatsDto
                {
                    Depth = queue.QueueDepth,
                    OutstandingDeliveries = queue.CurrentOutstandingDeliveries,
                    ConsumerCount = queue.CurrentSubscriberCount,
                    ReceivedCount = queue.ReceivedMessageCount.ToString(CultureInfo.InvariantCulture),
                    DeliveredCount = queue.DeliveredMessageCount.ToString(CultureInfo.InvariantCulture),
                    FailedCount = queue.FailedDeliveryCount.ToString(CultureInfo.InvariantCulture),
                    DeferredCount = queue.DeferredDeliveryCount.ToString(CultureInfo.InvariantCulture),
                    ExpiredCount = queue.ExpiredMessageCount.ToString(CultureInfo.InvariantCulture),
                    DroppedCount = queue.ExplicitDropCount.ToString(CultureInfo.InvariantCulture),
                    DeadLetteredCount = queue.ExplicitDeadLetterCount.ToString(CultureInfo.InvariantCulture)
                }
            };
        }

        public static bool IsJson(string contentType)
        {
            var mediaType = contentType.Split(';')[0].Trim();
            return mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
                || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
        }

        private static void SetBody(MessageDto dto, string body, bool bodyIsBase64)
        {
            if (bodyIsBase64)
            {
                dto.BodyBase64 = body;
            }
            else if (IsJson(dto.ContentType))
            {
                try
                {
                    using var document = JsonDocument.Parse(body);
                    dto.Body = document.RootElement.Clone();
                }
                catch (JsonException)
                {
                    dto.Body = body; //A publisher used a custom serializer, so it is passed through as text.
                }
            }
            else
            {
                dto.Body = body;
            }
        }

        public static MessageDto ToMessage(CMqEnqueuedMessageDescriptor message, bool includeBody)
        {
            var dto = new MessageDto
            {
                MessageId = message.MessageId,
                SerialNumber = message.SerialNumber.ToString(CultureInfo.InvariantCulture),
                MessageType = message.MessageType,
                ContentType = message.ContentType,
                Headers = message.Headers,
                EnqueuedAt = message.Timestamp,
                DeferredUntil = message.DeferredUntil
            };

            if (includeBody)
            {
                SetBody(dto, message.Body, message.BodyIsBase64);
            }

            return dto;
        }

        public static MessageDto ToMessage(CMqLeasedMessage leased)
        {
            var message = leased.Message;
            var dto = new MessageDto
            {
                MessageId = message.MessageId,
                SerialNumber = message.SerialNumber.ToString(CultureInfo.InvariantCulture),
                MessageType = message.MessageType,
                ContentType = message.ContentType,
                Headers = message.Headers,
                EnqueuedAt = message.Timestamp,
                DeliveryAttempt = message.DeliveryAttempt,
                LeaseId = leased.LeaseId,
                LeaseExpiresAt = leased.LeaseExpiresAt
            };

            SetBody(dto, message.Body, message.BodyIsBase64);
            return dto;
        }
    }
}
