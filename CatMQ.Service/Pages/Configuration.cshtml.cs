using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NTDLS.CatMQ.Server;
using NTDLS.CatMQ.Server.Management;

namespace CatMQ.Service.Pages
{
    [Authorize]
    public class ConfigurationModel(ILogger<ConfigurationModel> logger, CMqServer mqServer) : PageModel
    {
        private readonly ILogger<ConfigurationModel> _logger = logger;
        public string? ErrorMessage { get; set; }
        public CMqServerInformation ServerConfig = new();

        public void OnGet()
        {
            try
            {
                ServerConfig = mqServer.GetConfiguration();

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetQueues");
                ErrorMessage = ex.Message;
            }
        }
    }
}
