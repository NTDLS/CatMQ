using CatMQ.Service.Models.Page;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NTDLS.CatMQ.Server;

namespace CatMQ.Service.Pages
{
    [Authorize]
    public class RemoveSubscriptionModel(ILogger<RemoveSubscriptionModel> logger, CMqServer mqServer) : BasePageModel
    {
        private readonly ILogger<RemoveSubscriptionModel> _logger = logger;

        public string? RedirectURL { get; set; }

        [BindProperty]
        public string QueueName { get; set; } = string.Empty;

        [BindProperty]
        public Guid SubscriberId { get; set; }

        [BindProperty]
        public string? UserSelection { get; set; }

        public IActionResult OnPost()
        {
            RedirectURL = $"/Queue/{QueueName}";

            try
            {
                if (UserSelection?.Equals("true") == true)
                {
                    mqServer.Unsubscribe(SubscriberId, QueueName);
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
