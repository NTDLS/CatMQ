using Microsoft.AspNetCore.Mvc.RazorPages;
using NTDLS.PrudentMessageQueueServer;
using NTDLS.PrudentMessageQueueServer.Management;

namespace PrudentMessageQueueService.Pages
{
    public class IndexModel(ILogger<IndexModel> logger, PMqServer mqServer) : PageModel
    {
        private readonly ILogger<IndexModel> _logger = logger;
        public List<PMqQueueInformation> Queues { get; private set; } = new();
        public PMqServerInformation ServerConfig = new();
        public string? ErrorMessage { get; set; }

        public void OnGet()
        {
            try
            {
                ServerConfig = mqServer.GetConfiguration();

                var queues = mqServer.GetQueues();

                foreach (var queue in queues)
                {
                    Queues.Add(queue);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetQueues");
                ErrorMessage = ex.Message;
            }
        }
    }
}
