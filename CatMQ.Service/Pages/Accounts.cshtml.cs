using CatMQ.Service.Models.Data;
using CatMQ.Service.Models.Page;
using Microsoft.AspNetCore.Authorization;

namespace CatMQ.Service.Pages
{
    [Authorize]
    public class AccountsModel(ILogger<AccountsModel> logger, ServiceConfiguration serviceConfiguration) : BasePageModel
    {
        private readonly ILogger<AccountsModel> _logger = logger;
        public List<Account> Accounts { get; set; } = new();

        public void OnGet()
        {
            try
            {
                Accounts = serviceConfiguration.Read<List<Account>>("accounts.json", new());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetQueues");
                ErrorMessage = ex.Message;
            }
        }
    }
}
