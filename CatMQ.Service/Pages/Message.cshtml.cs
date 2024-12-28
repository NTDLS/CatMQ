using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NTDLS.CatMQ.Server;
using NTDLS.CatMQ.Server.Management;

namespace CatMQ.Service.Pages
{
    public class MessageModel(ILogger<MessageModel> logger, CMqServer mqServer) : PageModel
    {
        [BindProperty(SupportsGet = true)]
        public string QueueName { get; set; } = string.Empty;
        [BindProperty(SupportsGet = true)]
        public Guid MessageId { get; set; }

        public string? ErrorMessage { get; set; }

        private readonly ILogger<MessageModel> _logger = logger;
        public CMqEnqueuedMessageInformation Message { get; set; } = new();

        public void OnGet()
        {
            try
            {
                Message = mqServer.GetQueueMessage(QueueName, MessageId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetQueueMessage");
                ErrorMessage = ex.Message;
            }
        }
    }
}
