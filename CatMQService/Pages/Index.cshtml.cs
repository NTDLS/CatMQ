using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NTDLS.CatMQServer;
using NTDLS.CatMQServer.Management;

namespace CatMQService.Pages
{
    [Authorize]

    public class IndexModel(ILogger<IndexModel> logger, CMqServer mqServer) : PageModel
    {
        private readonly ILogger<IndexModel> _logger = logger;
        public List<CMqQueueInformation> Queues { get; private set; } = new();
        public CMqServerInformation ServerConfig = new();
        public string? ErrorMessage { get; set; }

        public void OnGet()
        {
            try
            {
                ServerConfig = mqServer.GetConfiguration();
                Queues = mqServer.GetQueues().OrderBy(o => o.QueueName).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetQueues");
                ErrorMessage = ex.Message;
            }
        }
    }
}
