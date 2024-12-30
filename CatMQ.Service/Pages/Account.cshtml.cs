using CatMQ.Service.Models.Data;
using CatMQ.Service.Models.Page;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using static CatMQ.Service.Configs;

namespace CatMQ.Service.Pages
{
    [Authorize]
    public class AccountModel(ILogger<AccountModel> logger) : BasePageModel
    {
        private readonly ILogger<AccountModel> _logger = logger;

        [BindProperty(SupportsGet = true)]
        public string AccountName { get; set; } = string.Empty;

        [BindProperty]
        public Account Account { get; set; } = new();

        public List<TimeZoneItem> TimeZones { get; set; } = new();

        [BindProperty]
        public string? Password { get; set; }

        public IActionResult OnPost()
        {
            try
            {
                if (ModelState.IsValid)
                {
                    var accounts = Configs.Read<List<Account>>(ConfigFile.Accounts, new());

                    if (!string.IsNullOrEmpty(Password))
                    {
                        Account.PasswordHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Password ?? string.Empty))).ToLower();
                    }
                    else
                    {
                        Account.PasswordHash = accounts.FirstOrDefault(o => o.Id == Account.Id)?.PasswordHash; //Preserve old password hash.
                    }

                    accounts.RemoveAll(o => o.Id == Account.Id);
                    accounts.Add(Account);

                    Configs.Write(ConfigFile.Accounts, accounts);

                    SuccessMessage = "Saved!";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetQueues");
                ErrorMessage = ex.Message;
            }

            TimeZones = TimeZoneItem.GetAll();

            return Page();
        }

        public void OnGet()
        {
            try
            {
                TimeZones = TimeZoneItem.GetAll();

                Account = Configs.Read<List<Account>>(ConfigFile.Accounts, new())
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
