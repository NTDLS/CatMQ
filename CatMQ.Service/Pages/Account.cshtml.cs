using CatMQ.Service.Models.Data;
using CatMQ.Service.Models.Page;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace CatMQ.Service.Pages
{
    [Authorize]
    public class AccountModel(ILogger<AccountModel> logger) : BasePageModel
    {
        private readonly ILogger<AccountModel> _logger = logger;

        [BindProperty(SupportsGet = true)]
        public Guid AccountId { get; set; }

        [BindProperty]
        public Account Account { get; set; } = new();

        public List<TimeZoneItem> TimeZones { get; set; } = new();

        [BindProperty]
        public string? Password { get; set; }

        [BindProperty]
        public Guid? ApiKeyId { get; set; }

        #region Confirm Action.

        [BindProperty]
        public string? UserSelection { get; set; }

        #endregion

        public IActionResult OnPostSaveAccount()
        {
            try
            {
                if (!string.IsNullOrEmpty(Password))
                {
                    if (!Utility.IsPasswordComplex(Password, out var errorMessage))
                    {
                        ModelState.AddModelError(nameof(Password), errorMessage);
                    }
                    else
                    {
                        Account.PasswordHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Password ?? string.Empty))).ToLower();
                    }
                }

                if (ModelState.IsValid)
                {
                    var accounts = Configs.GetAccounts();

                    if (string.IsNullOrEmpty(Password))
                    {
                        Account.PasswordHash = accounts.FirstOrDefault(o => o.Id == AccountId)?.PasswordHash; //Preserve old password hash.
                    }

                    Account.ApiKeys = accounts.FirstOrDefault(o => o.Id == AccountId)?.ApiKeys ?? new(); //Preserve old api keys.
                    Account.Id = AccountId; //Preserve accountId.

                    accounts.RemoveAll(o => o.Id == AccountId);
                    accounts.Add(Account);

                    Configs.PutAccounts(accounts);

                    SuccessMessage = "Saved!";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex.Message);
                ErrorMessage = ex.Message;
            }

            TimeZones = TimeZoneItem.GetAll();

            return Page();
        }

        public IActionResult OnPostCreateAPIKey()
        {
            try
            {
                var accounts = Configs.GetAccounts();

                if (ModelState.IsValid)
                {
                    Account = Configs.GetAccounts().Where(o => o.Id.Equals(AccountId)).Single();

                    Account.ApiKeys.Add(new AccountApiKey()
                    {
                        Id = Guid.NewGuid(),
                        Key = Utility.CreateApiKey(),
                        Description = string.Empty
                    });

                    Configs.PutAccounts(accounts);

                    SuccessMessage = "API Key Created!";
                }

                TimeZones = TimeZoneItem.GetAll();

                Account = Configs.GetAccounts().Where(o => o.Id.Equals(AccountId)).Single();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex.Message);
                ErrorMessage = ex.Message;
            }

            return Page();
        }

        public void OnGet()
        {
            try
            {
                TimeZones = TimeZoneItem.GetAll();

                Account = Configs.GetAccounts().Where(o => o.Id.Equals(AccountId)).Single();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex.Message);
                ErrorMessage = ex.Message;
            }
        }
    }
}
