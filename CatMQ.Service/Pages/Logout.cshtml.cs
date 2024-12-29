using CatMQ.Service.Models.Page;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CatMQ.Service.Pages
{
    [AllowAnonymous]
    public class LogoutModel(ILogger<LogoutModel> logger) : BasePageModel
    {
        private readonly ILogger<LogoutModel> _logger = logger;

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
