using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NTDLS.CatMQ.Server;
using NTDLS.CatMQ.Server.Management;

namespace CatMQ.Service.Pages
{
    [Authorize]
    public class AccountModel(ILogger<AccountModel> logger, ServiceConfiguration serviceConfiguration) : PageModel
    {
        private readonly ILogger<AccountModel> _logger = logger;
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
