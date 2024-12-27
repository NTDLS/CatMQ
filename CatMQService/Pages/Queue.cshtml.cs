using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NTDLS.CatMQServer;
using NTDLS.CatMQServer.Management;

namespace CatMQService.Pages
{
    [Authorize]
    public class QueueModel(ILogger<QueueModel> logger, CMqServer mqServer) : PageModel
    {
        [BindProperty(SupportsGet = true)]
        public string QueueName { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }

        private readonly ILogger<QueueModel> _logger = logger;
        public CMqQueueInformation Queue { get; private set; } = new();
        public List<CMqSubscriberInformation> Subscribers { get; set; } = new();

        public void OnGet()
        {
            try
            {
                Queue = mqServer.GetQueues().Where(o => o.QueueName.Equals(QueueName, StringComparison.OrdinalIgnoreCase)).FirstOrDefault() ?? new();
                Subscribers = mqServer.GetSubscribers(Queue.QueueName).OrderBy(o => o.ConnectionId).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetQueues/GetSubscribers");
                ErrorMessage = ex.Message;
            }
        }
    }
}
