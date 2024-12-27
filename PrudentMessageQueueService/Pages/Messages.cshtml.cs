using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NTDLS.PrudentMessageQueueServer;
using NTDLS.PrudentMessageQueueServer.Management;

namespace PrudentMessageQueueService.Pages
{
    public class MessagesModel(ILogger<MessagesModel> logger, PMqServer mqServer) : PageModel
    {
        const int PageSize = 20;

        [BindProperty(SupportsGet = true)]
        public string QueueName { get; set; } = string.Empty;
        [BindProperty(SupportsGet = true)]
        public int PageNumber { get; set; } = 0;
        public string? ErrorMessage { get; set; }

        private readonly ILogger<MessagesModel> _logger = logger;
        public List<PMqEnqueuedMessageInformation> Messages { get; set; } = new();

        public void OnGet()
        {
            try
            {
                Messages = mqServer.GetQueueMessages(QueueName, PageNumber * PageSize, PageSize).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetQueueMessages");
                ErrorMessage = ex.Message;
            }
        }
    }
}
