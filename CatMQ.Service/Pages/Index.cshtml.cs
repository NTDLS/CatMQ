using CatMQ.Service.Models.Page;
using Microsoft.AspNetCore.Authorization;
using NTDLS.CatMQ.Server;
using NTDLS.CatMQ.Server.Management;
using System.Reflection;

namespace CatMQ.Service.Pages
{
    [Authorize]

    public class IndexModel(ILogger<IndexModel> logger, CMqServer mqServer) : BasePageModel
    {
        private readonly ILogger<IndexModel> _logger = logger;
        public List<CMqQueueInformation> Queues { get; private set; } = new();
        public CMqServerInformation ServerConfig = new();

        public void OnGet()
        {
            try
            {
                ServerConfig = mqServer.GetConfiguration();
                Queues = mqServer.GetQueues().OrderBy(o => o.QueueName).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, MethodBase.GetCurrentMethod()?.Name ?? string.Empty);
                ErrorMessage = ex.Message;
            }
        }
    }
}
