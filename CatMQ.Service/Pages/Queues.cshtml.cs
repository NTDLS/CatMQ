using CatMQ.Service.Models.Page;
using Microsoft.AspNetCore.Authorization;
using NTDLS.CatMQ.Server;
using NTDLS.CatMQ.Server.Management;

namespace CatMQ.Service.Pages
{
    [Authorize]

    public class QueuesModel(ILogger<QueuesModel> logger, CMqServer mqServer) : BasePageModel
    {
        private readonly ILogger<QueuesModel> _logger = logger;
        public List<CMqQueueDescriptor> Queues { get; private set; } = new();

        public void OnGet()
        {
            try
            {
                Queues = mqServer.GetQueues()?.OrderBy(o => o.QueueName)?.ToList() ?? new();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex.Message);
                ErrorMessage = ex.Message;
            }
        }
    }
}
