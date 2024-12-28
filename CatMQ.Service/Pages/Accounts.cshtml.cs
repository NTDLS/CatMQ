using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CatMQ.Service.Pages
{
    [Authorize]
    public class AccountsModel(ILogger<AccountsModel> logger, ServiceConfiguration serviceConfiguration) : PageModel
    {
        private readonly ILogger<AccountsModel> _logger = logger;
        public string? ErrorMessage { get; set; }

        public void OnGet()
        {
            try
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetQueues");
                ErrorMessage = ex.Message;
            }
        }
    }
}
