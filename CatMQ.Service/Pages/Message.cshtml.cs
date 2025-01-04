using CatMQ.Service.Models.Page;
using Microsoft.AspNetCore.Mvc;
using NTDLS.CatMQ.Server;
using NTDLS.CatMQ.Server.Management;
using System.Reflection;

namespace CatMQ.Service.Pages
{
    public class MessageModel(ILogger<MessageModel> logger, CMqServer mqServer) : BasePageModel
    {
        [BindProperty(SupportsGet = true)]
        public string QueueName { get; set; } = string.Empty;
        [BindProperty(SupportsGet = true)]
        public Guid MessageId { get; set; }

        private readonly ILogger<MessageModel> _logger = logger;
        public CMqEnqueuedMessageDescriptor Message { get; set; } = new();

        public void OnGet()
        {
            try
            {
                Message = mqServer.GetQueueMessage(QueueName, MessageId) ?? new();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, MethodBase.GetCurrentMethod()?.Name ?? string.Empty);
                ErrorMessage = ex.Message;
            }
        }
    }
}
