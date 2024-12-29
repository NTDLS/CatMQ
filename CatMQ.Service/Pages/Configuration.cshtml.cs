using CatMQ.Service.Models.Page;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CatMQ.Service.Pages
{
    [Authorize]
    public class ConfigurationModel(ILogger<ConfigurationModel> logger, ServiceConfiguration serviceConfiguration) : BasePageModel
    {
        private readonly ILogger<ConfigurationModel> _logger = logger;

        [BindProperty]
        public ServiceConfiguration ServerConfig { get; set; } = new();

        public IActionResult OnPost()
        {
            try
            {
                if (ModelState.IsValid)
                {
                    serviceConfiguration.Write(ConfigFile.Service, ServerConfig);
                    SuccessMessage = "Saved!<br />You will need to restart the service for these changes to take affect.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetQueues");
                ErrorMessage = ex.Message;
            }

            return Page();
        }

        public void OnGet()
        {
            try
            {
                ServerConfig = serviceConfiguration.Read(ConfigFile.Service, new ServiceConfiguration());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetQueues");
                ErrorMessage = ex.Message;
            }
        }
    }
}
