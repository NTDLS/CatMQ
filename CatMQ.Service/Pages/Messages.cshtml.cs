using CatMQ.Service.Models.Page;
using Microsoft.AspNetCore.Mvc;
using NTDLS.CatMQ.Server;
using NTDLS.CatMQ.Server.Management;
using System.Reflection;

namespace CatMQ.Service.Pages
{
    public class MessagesModel(ILogger<MessagesModel> logger, CMqServer mqServer) : BasePageModel
    {
        const int PageSize = 20;

        [BindProperty(SupportsGet = true)]
        public string QueueName { get; set; } = string.Empty;
        [BindProperty(SupportsGet = true)]
        public int PageNumber { get; set; } = 0;

        private readonly ILogger<MessagesModel> _logger = logger;
        public List<CMqEnqueuedMessageInformation> Messages { get; set; } = new();

        public void OnGet()
        {
            try
            {
                Messages = mqServer.GetQueueMessages(QueueName, PageNumber * PageSize, PageSize).OrderBy(o => o.Timestamp).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, MethodBase.GetCurrentMethod()?.Name ?? string.Empty);
                ErrorMessage = ex.Message;
            }
        }
    }
}
