using CatMQ.Service.Models.Data;
using CatMQ.Service.Models.Page;
using Microsoft.AspNetCore.Authorization;
using System.Reflection;

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
                _logger.LogError(ex, MethodBase.GetCurrentMethod()?.Name ?? string.Empty);
                ErrorMessage = ex.Message;
            }
        }
    }
}
