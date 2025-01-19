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
    }
}
