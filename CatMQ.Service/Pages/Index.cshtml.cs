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
        public List<CMqQueueDescriptor> Queues { get; private set; } = new();
        public CMqServerDescriptor ServerConfig = new();
        public string ApplicationVersion { get; private set; } = string.Empty;

        public void OnGet()
        {
            ApplicationVersion = string.Join('.', (Assembly.GetExecutingAssembly()
                .GetName().Version?.ToString() ?? "0.0.0.0").Split('.').Take(3)); //Major.Minor.Patch

            try
            {
                ServerConfig = mqServer.GetConfiguration();
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
