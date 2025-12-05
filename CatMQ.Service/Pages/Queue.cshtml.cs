using CatMQ.Service.Models.Page;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NTDLS.CatMQ.Server;
using NTDLS.CatMQ.Server.Management;

namespace CatMQ.Service.Pages
{
    [Authorize]
    public class QueueModel(ILogger<QueueModel> logger, CMqServer mqServer) : BasePageModel
    {
        [BindProperty(SupportsGet = true)]
        public string QueueName { get; set; } = string.Empty;

        private readonly ILogger<QueueModel> _logger = logger;
        public CMqQueueDescriptor Queue { get; private set; } = new();
        public List<CMqSubscriberDescriptor> Subscribers { get; set; } = new();

        public void OnGet()
        {
            try
            {
                Queue = mqServer.GetQueues()?.Where(o => o.QueueName.Equals(QueueName, StringComparison.OrdinalIgnoreCase)).FirstOrDefault() ?? new();
                Subscribers = mqServer.GetSubscribers(Queue.QueueName)?.OrderBy(o => o.SubscriberId)?.ToList() ?? new();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex.Message);
                ErrorMessage = ex.Message;
            }
        }

        #region Queue Data.

        public JsonResult OnGetQueueData(string queueName)
        {
            var queue = mqServer.GetQueues()?
                .Where(o => o.QueueName.Equals(QueueName, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault() ?? new();
         
            return new JsonResult(queue);
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
