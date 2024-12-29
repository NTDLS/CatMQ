using CatMQ.Service.Models.Data;
using CatMQ.Service.Models.Page;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CatMQ.Service.Pages
{
    [Authorize]
    public class AccountModel(ILogger<AccountModel> logger, ServiceConfiguration serviceConfiguration) : BasePageModel
    {
        private readonly ILogger<AccountModel> _logger = logger;

        [BindProperty(SupportsGet = true)]
        public string AccountName { get; set; } = string.Empty;

        [BindProperty]
        public Account Account { get; set; } = new();

        public IActionResult OnPost()
        {
            try
            {
                if (ModelState.IsValid)
                {
                    var accounts = serviceConfiguration.Read<List<Account>>(ConfigFile.Accounts, new());

                    accounts.RemoveAll(o => o.Id == Account.Id);
                    accounts.Add(Account);

                    serviceConfiguration.Write(ConfigFile.Accounts, accounts);

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
                Account = serviceConfiguration.Read<List<Account>>(ConfigFile.Accounts, new())
                    .Where(o => o.Username.Equals(AccountName, StringComparison.OrdinalIgnoreCase)).FirstOrDefault()
                    ?? throw new Exception("Account was not found.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetQueues");
                ErrorMessage = ex.Message;
            }
        }
    }
}
