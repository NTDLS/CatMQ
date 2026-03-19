using CatMQ.Service.Models.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CatMQ.Service.Pages
{
    [Authorize]
    public class AccountsModel(ILogger<AccountsModel> logger)
        : PageModel
    {
        public List<Account> Accounts { get; set; } = new();
        public string? ErrorMessage { get; set; }

        public void OnGet()
        {
            try
            {
                Accounts = Configs.GetAccounts();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting accounts");
                ErrorMessage = ex.Message;
            }
        }
    }
}
