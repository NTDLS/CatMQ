using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NTDLS.CatMQ.Server;

namespace CatMQ.Service.Pages
{
    [Authorize]
    public class QueuesModel(ILogger<QueuesModel> logger, CMqServer mqServer)
        : PageModel
    {
        public string? ErrorMessage { get; set; }

        public void OnGet()
        {
            try
            {
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error fetching queues data");
                ErrorMessage = ex.Message;
            }
        }

        public JsonResult OnGetQueues()
        {
            var queues = mqServer.GetQueues()?.OrderBy(o => o.QueueName)?.ToList() ?? new();

            List<object> records = new();

            foreach (var queue in queues)
            {
                records.Add(new
                {
                    errorMessage = queue.ErrorMessage,
                    queueName = queue.QueueName,
                    currentSubscriberCount = queue.CurrentSubscriberCount.ToString("n0"),
                    queueDepth = queue.QueueDepth.ToString("n0"),
                    currentOutstandingDeliveries = queue.CurrentOutstandingDeliveries.ToString("n0"),
                    receivedMessageCount = queue.ReceivedMessageCount.ToString("n0"),
                    deliveredMessageCount = queue.DeliveredMessageCount.ToString("n0"),
                    deferredDeliveryCount = queue.DeferredDeliveryCount.ToString("N0"),
                    expiredMessageCount = queue.ExpiredMessageCount.ToString("n0"),
                    failedDeliveryCount = queue.FailedDeliveryCount.ToString("n0"),
                });
            }

            return new JsonResult(records);
        }
    }
}
