using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NTDLS.CatMQServer;

namespace CatMQService.Pages
{
    [AllowAnonymous]
    public class LogoutModel(ILogger<LogoutModel> logger, CMqServer mqServer) : PageModel
    {
        private readonly ILogger<LogoutModel> _logger = logger;

        public string? ErrorMessage { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            try
            {
                await HttpContext.SignOutAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Logout");
                ErrorMessage = ex.Message;
            }
            return RedirectToPage("/Login");
        }
    }
}
