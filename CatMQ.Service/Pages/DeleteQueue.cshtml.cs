using CatMQ.Service.Models.Page;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NTDLS.CatMQ.Server;
using System.Reflection;

namespace CatMQ.Service.Pages
{
    [Authorize]
    public class DeleteQueueModel(ILogger<DeleteQueueModel> logger, CMqServer mqServer) : BasePageModel
    {
        private readonly ILogger<DeleteQueueModel> _logger = logger;

        public string? RedirectURL { get; set; }

        [BindProperty]
        public string QueueName { get; set; } = string.Empty;

        [BindProperty]
        public string? UserSelection { get; set; }

        public IActionResult OnPost()
        {
            RedirectURL = $"/Queues";

            try
            {
                if (UserSelection?.Equals("true") == true)
                {
                    mqServer.DeleteQueue(QueueName);
                }
                else
                {
                    return Redirect(RedirectURL);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex.Message);
                ErrorMessage = ex.Message;
            }

            return Page();
        }
    }
}
