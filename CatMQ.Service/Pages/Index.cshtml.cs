using CatMQ.Service.Models.Page;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NTDLS.CatMQ.Server;
using NTDLS.CatMQ.Server.Management;
using System.Reflection;

namespace CatMQ.Service.Pages
{
    [Authorize]

    public class IndexModel(ILogger<IndexModel> logger, CMqServer mqServer) : BasePageModel
    {
        private readonly ILogger<IndexModel> _logger = logger;
        public CMqServerDescriptor ServerConfig = new();
        public string ApplicationVersion { get; private set; } = string.Empty;

        public void OnGet()
        {
            ApplicationVersion = string.Join('.', (Assembly.GetExecutingAssembly()
                .GetName().Version?.ToString() ?? "0.0.0.0").Split('.').Take(3)); //Major.Minor.Patch

            try
            {
                ServerConfig = mqServer.GetConfiguration();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex.Message);
                ErrorMessage = ex.Message;
            }
        }

        public JsonResult OnGetQueues()
        {
            var queues = mqServer.GetQueues()?.OrderBy(o => o.QueueName)?.ToList() ?? new();

            List<object> queueRecords = new();

            long totalSubscribers = 0;
            long totalDepth = 0;
            long totalOutstanding = 0;
            ulong totalReceived = 0;
            ulong totalDelivered = 0;
            ulong totalExpired = 0;
            ulong totalFailed = 0;
            ulong totalDeferred = 0;

            foreach (var queue in queues)
            {
                totalSubscribers += queue.CurrentSubscriberCount;
                totalDepth += queue.QueueDepth;
                totalOutstanding += queue.CurrentOutstandingDeliveries;
                totalReceived += queue.ReceivedMessageCount;
                totalDelivered += queue.DeliveredMessageCount;
                totalExpired += queue.ExpiredMessageCount;
                totalFailed += queue.FailedDeliveryCount;
                totalDeferred += queue.DeferredDeliveryCount;

                queueRecords.Add(new
                {
                    queueName = queue.QueueName,
                    currentSubscriberCount = queue.CurrentSubscriberCount.ToString("n0"),
                    queueDepth = queue.QueueDepth.ToString("n0"),
                    currentOutstandingDeliveries = queue.CurrentOutstandingDeliveries.ToString("n0"),
                    receivedMessageCount = queue.ReceivedMessageCount.ToString("n0"),
                    deliveredMessageCount = queue.DeliveredMessageCount.ToString("n0"),
                    expiredMessageCount = queue.ExpiredMessageCount.ToString("n0"),
                    failedDeliveryCount = queue.FailedDeliveryCount.ToString("n0"),
                    deferredDeliveryCount = queue.DeferredDeliveryCount.ToString("n0")
                });
            }

            var totals = new
            {
                currentSubscriberCount = totalSubscribers.ToString("n0"),
                queueDepth = totalDepth.ToString("n0"),
                currentOutstandingDeliveries = totalOutstanding.ToString("n0"),
                receivedMessageCount = totalReceived.ToString("n0"),
                deliveredMessageCount = totalDelivered.ToString("n0"),
                expiredMessageCount = totalExpired.ToString("n0"),
                failedDeliveryCount = totalFailed.ToString("n0"),
                deferredDeliveryCount = totalDeferred.ToString("n0"),
            };

            return new JsonResult(new
            {
                queues = queueRecords,
                totals = totals
            });
        }

        #region Chart Data.

        public JsonResult OnGetChartData(string queueName)
        {
            var history = mqServer.GetAllQueuesHistoricalStatistics();

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

            var deferredValues = ordered
                .Select(kvp => (double)kvp.Value.DeferredDeliveries)
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
                },
                new ChartSeriesDto
                {
                    Label = "Deferred",
                    Values = deferredValues
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
