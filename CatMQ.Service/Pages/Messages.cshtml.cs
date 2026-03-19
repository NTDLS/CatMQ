using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NTDLS.CatMQ.Server;
using NTDLS.CatMQ.Server.Management;

namespace CatMQ.Service.Pages
{
    public class MessagesModel(ILogger<MessagesModel> logger, CMqServer mqServer)
        : PageModel
    {
        const int PageSize = 20;

        [BindProperty(SupportsGet = true)]
        public string QueueName { get; set; } = string.Empty;
        [BindProperty(SupportsGet = true)]
        public int PageNumber { get; set; } = 0;
        public int TotalPages { get; set; } = 0;

        public List<CMqEnqueuedMessageDescriptor> Messages { get; set; } = new();
        public string? ErrorMessage { get; set; }

        public void OnGet()
        {
            try
            {
                var descriptorCollection = mqServer.GetQueueMessages(QueueName, PageNumber * PageSize, PageSize);
                Messages = descriptorCollection?.Messages.OrderBy(o => o.Timestamp)?.ToList() ?? new();

                TotalPages = (int)Math.Ceiling(((descriptorCollection?.QueueDepth / (double)PageSize) - 1) ?? 0.0);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error fetching messages");
                ErrorMessage = ex.Message;
            }
        }
    }
}
