using CatMQ.Service.Models.Data;
using CatMQ.Service.Models.Page;
using Microsoft.AspNetCore.Authorization;

namespace CatMQ.Service.Pages
{
    [Authorize]
    public class AccountsModel(ILogger<AccountsModel> logger) : BasePageModel
    {
        private readonly ILogger<AccountsModel> _logger = logger;
        public List<Account> Accounts { get; set; } = new();

        public void OnGet()
        {
            try
            {
                Accounts = Configs.GetAccounts();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex.Message);
                ErrorMessage = ex.Message;
            }
        }
    }
}
