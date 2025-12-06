using CatMQ.Service.Models.Page;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NTDLS.CatMQ.Server;
using NTDLS.Helpers;

namespace CatMQ.Service.Pages
{
    [Authorize]
    public class QueueModel(ILogger<QueueModel> logger, CMqServer mqServer) : BasePageModel
    {
        [BindProperty(SupportsGet = true)]
        public string QueueName { get; set; } = string.Empty;

        private readonly ILogger<QueueModel> _logger = logger;

        public void OnGet()
        {
            try
            {
                //Get the queue name (case insensitive) from the server to ensure it exists.
                QueueName = mqServer.GetQueue(QueueName)?.QueueName ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex.Message);
                ErrorMessage = ex.Message;
            }
        }

        #region Queue Subscribers.

        public JsonResult OnGetQueueSubscribers(string queueName)
        {
            var subscribers = mqServer.GetSubscribers(queueName)?.OrderBy(o => o.SubscriberId)?.ToList() ?? new();

            List<object> records = new();

            foreach (var subscriber in subscribers)
            {
                records.Add(new
                {
                    SubscriberId = subscriber.SubscriberId,
                    remotePeer = $"{subscriber.RemoteAddress} : {subscriber.RemotePort}",
                    attemptedDeliveryCount = subscriber.AttemptedDeliveryCount.ToString("n0"),
                    successfulDeliveryCount = subscriber.SuccessfulDeliveryCount.ToString("n0"),
                    deferredDeliveryCount = subscriber.DeferredDeliveryCount.ToString("n0"),
                    consumedDeliveryCount = subscriber.ConsumedDeliveryCount.ToString("n0"),
                    failedDeliveryCount = subscriber.FailedDeliveryCount.ToString("n0")
                });
            }

            return new JsonResult(records);
        }

        #endregion

        #region Queue Data.

        public JsonResult OnGetQueueData(string queueName)
        {
            var queue = mqServer.GetQueues()?
                .Where(o => o.QueueName.Equals(QueueName, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault() ?? new();

            var record = new
            {
                queueName = queue.QueueName,
                deliveryThrottle = queue.DeliveryThrottle.ToString(),
                maxOutstandingDeliveries = queue.MaxOutstandingDeliveries.ToString("N0"),
                maxDeliveryAttempts = queue.MaxDeliveryAttempts.ToString("N0"),
                maxMessageAge = queue.MaxMessageAge?.ToString() ?? "\u221E",
                consumptionScheme = Text.SeparateCamelCase(queue.ConsumptionScheme.ToString()),
                deliveryScheme = Text.SeparateCamelCase(queue.DeliveryScheme.ToString()),
                queueDepth = queue.QueueDepth.ToString("N0"),
                currentSubscriberCount = queue.CurrentSubscriberCount.ToString("N0"),
                currentOutstandingDeliveries = queue.CurrentOutstandingDeliveries.ToString("N0"),
                receivedMessageCount = queue.ReceivedMessageCount.ToString("N0"),
                expiredMessageCount = queue.ExpiredMessageCount.ToString("N0"),
                deliveredMessageCount = queue.DeliveredMessageCount.ToString("N0"),
                failedDeliveryCount = queue.FailedDeliveryCount.ToString("N0"),
                deferredDeliveryCount = queue.DeferredDeliveryCount.ToString("N0"),
                explicitDropCount = queue.ExplicitDropCount.ToString("N0"),
                explicitDeadLetterCount = queue.ExplicitDeadLetterCount.ToString("N0"),
                persistenceScheme = Text.SeparateCamelCase(queue.PersistenceScheme.ToString())
            };

            return new JsonResult(record);
        }

        #endregion

        #region Chart Data.

        public JsonResult OnGetChartData(string queueName)
        {
            var history = mqServer.GetQueueHistoricalStatistics(queueName);

            if (history == null || history.Count == 0)
            {
                var empty = new ChartDataDto
                {
                    Labels = Array.Empty<string>(),
                    Series = Array.Empty<ChartSeriesDto>()
                };
                return new JsonResult(empty);
            }

            var ordered = history
                .OrderBy(kvp => kvp.Key)
                .ToList();

            var labels = ordered
                .Select(kvp => kvp.Key.ToLocalTime().ToString("HH:mm"))
                .ToArray();

            var enqueuedValues = ordered
                .Select(kvp => (double)kvp.Value.EnqueuedCount)
                .ToArray();

            var deliveredValues = ordered
                .Select(kvp => (double)kvp.Value.DeliveryCount)
                .ToArray();

            var dequeuedValues = ordered
                .Select(kvp => (double)kvp.Value.DequeuedCount)
                .ToArray();

            var queueDepthValues = ordered
                .Select(kvp => (double)kvp.Value.QueueDepth)
                .ToArray();

            var outstandingValues = ordered
                .Select(kvp => (double)kvp.Value.OutstandingDeliveries)
                .ToArray();

            var series = new[]
            {
                new ChartSeriesDto
                {
                    Label = "Enqueued",
                    Values = enqueuedValues
                },
                new ChartSeriesDto
                {
                    Label = "Delivered",
                    Values = deliveredValues
                },
                new ChartSeriesDto
                {
                    Label = "Dequeued",
                    Values = dequeuedValues
                },
                new ChartSeriesDto
                {
                    Label = "Depth",
                    Values = queueDepthValues
                },
                new ChartSeriesDto
                {
                    Label = "Outstanding",
                    Values = outstandingValues
                }
            };

            var result = new ChartDataDto
            {
                Labels = labels,
                Series = series
            };

            return new JsonResult(result);
        }

        public class ChartDataDto
        {
            public IEnumerable<string> Labels { get; set; } = Enumerable.Empty<string>();
            public IEnumerable<ChartSeriesDto> Series { get; set; } = Enumerable.Empty<ChartSeriesDto>();
        }

        public class ChartSeriesDto
        {
            public string Label { get; set; } = string.Empty;
            public IEnumerable<double> Values { get; set; } = Enumerable.Empty<double>();
        }

        #endregion
    }
}
