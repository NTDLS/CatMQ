using CatMQ.Service.Models.Page;
using Microsoft.AspNetCore.Authorization;

namespace CatMQ.Service.Pages
{
    [Authorize]
    public class AccountModel(ILogger<AccountModel> logger, ServiceConfiguration serviceConfiguration) : BasePageModel
    {
        private readonly ILogger<AccountModel> _logger = logger;

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
