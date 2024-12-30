using CatMQ.Service.Models.Page;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using static CatMQ.Service.Configs;

namespace CatMQ.Service.Pages
{
    [Authorize]
    public class ConfigurationModel(ILogger<ConfigurationModel> logger) : BasePageModel
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
                    Configs.Write(ConfigFile.Service, ServerConfig);
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
                ServerConfig = Configs.Read(ConfigFile.Service, new ServiceConfiguration());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetQueues");
                ErrorMessage = ex.Message;
            }
        }
    }
}
