using CatMQ.Service.Models.Page;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NTDLS.CatMQ.Server;
using System.Reflection;

namespace CatMQ.Service.Pages
{
    [Authorize]
    public class PurgeQueueModel(ILogger<PurgeQueueModel> logger, CMqServer mqServer) : BasePageModel
    {
        private readonly ILogger<PurgeQueueModel> _logger = logger;

        public string? RedirectURL { get; set; }

        [BindProperty]
        public string QueueName { get; set; } = string.Empty;

        [BindProperty]
        public string? UserSelection { get; set; }

        public IActionResult OnPost()
        {
            RedirectURL = $"/Queue/{QueueName}";

            try
            {
                if (UserSelection?.Equals("true") == true)
                {
                    mqServer.PurgeQueue(QueueName);
                }
                else
                {
                    return Redirect(RedirectURL);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, MethodBase.GetCurrentMethod()?.Name ?? string.Empty);
                ErrorMessage = ex.Message;
            }

            return Page();
        }
    }
}
