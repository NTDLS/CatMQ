using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NTDLS.CatMQServer;
using NTDLS.CatMQServer.Management;

namespace CatMQService.Pages
{
    public class MessagesModel(ILogger<MessagesModel> logger, CMqServer mqServer) : PageModel
    {
        const int PageSize = 20;

        [BindProperty(SupportsGet = true)]
        public string QueueName { get; set; } = string.Empty;
        [BindProperty(SupportsGet = true)]
        public int PageNumber { get; set; } = 0;
        public string? ErrorMessage { get; set; }

        private readonly ILogger<MessagesModel> _logger = logger;
        public List<CMqEnqueuedMessageInformation> Messages { get; set; } = new();

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
