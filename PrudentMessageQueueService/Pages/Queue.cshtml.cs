using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NTDLS.PrudentMessageQueueServer;
using NTDLS.PrudentMessageQueueServer.Management;

namespace PrudentMessageQueueService.Pages
{
    [Authorize]
    public class QueueModel(ILogger<QueueModel> logger, PMqServer mqServer) : PageModel
    {
        [BindProperty(SupportsGet = true)]
        public string QueueName { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }

        private readonly ILogger<QueueModel> _logger = logger;
        public PMqQueueInformation Queue { get; private set; } = new();
        public List<PMqSubscriberInformation> Subscribers { get; set; } = new();

        public void OnGet()
        {
            try
            {
                Queue = mqServer.GetQueues().Where(o => o.QueueName.Equals(QueueName, StringComparison.OrdinalIgnoreCase)).FirstOrDefault() ?? new();
                Subscribers = mqServer.GetSubscribers(Queue.QueueName).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetQueues/GetSubscribers");
                ErrorMessage = ex.Message;
            }
        }
    }
}
