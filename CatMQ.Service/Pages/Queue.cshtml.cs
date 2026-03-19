using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NTDLS.CatMQ.Server;
using NTDLS.Helpers;

namespace CatMQ.Service.Pages
{
    [Authorize]
    public class QueueModel(ILogger<QueueModel> logger, CMqServer mqServer)
        : PageModel
    {
        [BindProperty(SupportsGet = true)]
        public string QueueName { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }

        public void OnGet()
        {
            try
            {
                var queue = mqServer.GetQueue(QueueName);
                QueueName = queue?.QueueName ?? string.Empty;
                ErrorMessage = queue?.ErrorMessage;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error fetching queue data");
                ErrorMessage = ex.Message;
            }
        }

        #region Queue Subscribers.

        public JsonResult OnGetQueueSubscribers(string queueName)
        {
            var subscribers = mqServer.GetSubscribers(queueName)?.OrderBy(o => o.SubscriberId)?.ToList() ?? new();

            var records = new List<object>();

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

        public JsonResult OnGetQueueData()
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
                .Select(kvp => kvp.Key.ToLocalTime().ToString("HH:mm:ss"))
                .ToArray();

            var receiveRates = ordered
                .Select(kvp => (double)kvp.Value.ReceiveRate)
                .ToArray();

            var deliveryRates = ordered
                .Select(kvp => (double)kvp.Value.DeliveryRate)
                .ToArray();

            var deferredDeliveryRates = ordered
                .Select(kvp => (double)kvp.Value.DeferredDeliveryRate)
                .ToArray();

            var queueDepths = ordered
                .Select(kvp => (double)kvp.Value.QueueDepth)
                .ToArray();

            var OutstandingDeliveries = ordered
                .Select(kvp => (double)kvp.Value.OutstandingDeliveries)
                .ToArray();

            var series = new[]
            {
                new ChartSeriesDto
                {
                    Label = "Receive/s",
                    Values = receiveRates,
                    YAxisId = "y"
                },
                new ChartSeriesDto
                {
                    Label = "Delivered/s",
                    Values = deliveryRates,
                    YAxisId = "y"
                },
                new ChartSeriesDto
                {
                    Label = "Deferred/s",
                    Values = deferredDeliveryRates,
                    YAxisId = "y"
                },
                new ChartSeriesDto
                {
                    Label = "Depth",
                    Values = queueDepths,
                    YAxisId = "y1"
                },
                new ChartSeriesDto
                {
                    Label = "Outstanding",
                    Values = OutstandingDeliveries,
                    YAxisId = "y"
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
            public string YAxisId { get; set; } = string.Empty;
            public string Label { get; set; } = string.Empty;
            public IEnumerable<double> Values { get; set; } = Enumerable.Empty<double>();
        }

        #endregion
    }
}
