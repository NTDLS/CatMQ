using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NTDLS.CatMQ.Server;
using NTDLS.CatMQ.Server.Management;

namespace CatMQ.Service.Pages
{
    [Authorize]

    public class QueuesModel(ILogger<QueuesModel> logger, CMqServer mqServer) : PageModel
    {
        private readonly ILogger<QueuesModel> _logger = logger;
        public List<CMqQueueInformation> Queues { get; private set; } = new();
        public string? ErrorMessage { get; set; }

        public void OnGet()
        {
            try
            {
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
