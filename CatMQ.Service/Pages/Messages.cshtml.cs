using CatMQ.Service.Models.Page;
using Microsoft.AspNetCore.Mvc;
using NTDLS.CatMQ.Server;
using NTDLS.CatMQ.Server.Management;

namespace CatMQ.Service.Pages
{
    public class MessagesModel(ILogger<MessagesModel> logger, CMqServer mqServer) : BasePageModel
    {
        const int PageSize = 20;

        [BindProperty(SupportsGet = true)]
        public string QueueName { get; set; } = string.Empty;
        [BindProperty(SupportsGet = true)]
        public int PageNumber { get; set; } = 0;
        public int TotalPages { get; set; } = 0;

        private readonly ILogger<MessagesModel> _logger = logger;
        public List<CMqEnqueuedMessageDescriptor> Messages { get; set; } = new();

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
                _logger.LogWarning(ex.Message);
                ErrorMessage = ex.Message;
            }
        }
    }
}
