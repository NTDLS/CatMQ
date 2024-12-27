using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NTDLS.PrudentMessageQueueServer;
using NTDLS.PrudentMessageQueueServer.Management;

namespace PrudentMessageQueueService.Pages
{
    public class MessageModel(ILogger<IndexModel> logger, PMqServer mqServer) : PageModel
    {
        [BindProperty(SupportsGet = true)]
        public string QueueName { get; set; } = string.Empty;
        [BindProperty(SupportsGet = true)]
        public Guid MessageId { get; set; }

        public string? ErrorMessage { get; set; }

        private readonly ILogger<IndexModel> _logger = logger;
        public PMqEnqueuedMessageInformation Message { get; set; } = new();

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
