using CatMQ.Service.Models.Page;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NTDLS.CatMQ.Server;
using NTDLS.CatMQ.Server.Management;

namespace CatMQ.Service.Pages
{
    [Authorize]
    public class IndexModel(ILogger<IndexModel> logger, CMqServer mqServer) : BasePageModel
    {
        private readonly ILogger<IndexModel> _logger = logger;
        public CMqServerDescriptor ServerConfig = new();

        public void OnGet()
        {

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

        public JsonResult OnGetChartData()
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
                    Values = receiveRates
                },
                new ChartSeriesDto
                {
                    Label = "Delivered/s",
                    Values = deliveryRates
                },
                new ChartSeriesDto
                {
                    Label = "Deferred/s",
                    Values = deferredDeliveryRates
                },
                new ChartSeriesDto
                {
                    Label = "Depth",
                    Values = queueDepths
                },
                new ChartSeriesDto
                {
                    Label = "Outstanding",
                    Values = OutstandingDeliveries
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
