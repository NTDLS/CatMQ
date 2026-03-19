using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NTDLS.CatMQ.Server;
using NTDLS.CatMQ.Server.Management;

namespace CatMQ.Service.Pages
{
    public class MessageModel(ILogger<MessageModel> logger, CMqServer mqServer)
        : PageModel
    {
        [BindProperty(SupportsGet = true)]
        public string QueueName { get; set; } = string.Empty;

        [BindProperty(SupportsGet = true)]
        public ulong SerialNumber { get; set; }

        public CMqEnqueuedMessageDescriptor? Message { get; set; }
        public string? ErrorMessage { get; set; }

        public void OnGet()
        {
            try
            {
                Message = mqServer.GetQueueMessage(QueueName, SerialNumber, 1024 * 1024);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error fetching message");
                ErrorMessage = ex.Message;
            }
        }
    }
}
