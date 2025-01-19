using CatMQ.Service.Models.Page;
using Microsoft.AspNetCore.Mvc;
using NTDLS.CatMQ.Server;
using NTDLS.CatMQ.Server.Management;

namespace CatMQ.Service.Pages
{
    public class MessageModel(ILogger<MessageModel> logger, CMqServer mqServer) : BasePageModel
    {
        [BindProperty(SupportsGet = true)]
        public string QueueName { get; set; } = string.Empty;
        [BindProperty(SupportsGet = true)]
        public ulong SerialNumber { get; set; }

        private readonly ILogger<MessageModel> _logger = logger;
        public CMqEnqueuedMessageDescriptor? Message { get; set; }

        public void OnGet()
        {
            try
            {
                Message = mqServer.GetQueueMessage(QueueName, SerialNumber, 1024 * 1024);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex.Message);
                ErrorMessage = ex.Message;
            }
        }
    }
}
