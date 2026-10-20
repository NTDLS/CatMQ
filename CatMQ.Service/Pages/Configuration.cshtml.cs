using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CatMQ.Service.Pages
{
    [Authorize]
    public class ConfigurationModel(ILogger<ConfigurationModel> logger)
        : PageModel
    {
        [BindProperty]
        public ServiceConfiguration ServerConfig { get; set; } = new();
        public string? ErrorMessage { get; set; }
        public string? SuccessMessage { get; set; }

        public IActionResult OnPost()
        {
            try
            {
                if (ModelState.IsValid)
                {
                    Configs.PutServiceConfig(ServerConfig);
                    SuccessMessage = Localize("Configuration has been saved.") + "<br />" + Localize("You will need to restart the service for these changes to take effect.");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error saving configuration");
                ErrorMessage = ex.Message;
            }

            return Page();
        }

        public void OnGet()
        {
            try
            {
                ServerConfig = Configs.GetServiceConfig();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting configuration");
                ErrorMessage = ex.Message;
            }
        }
    }
}
