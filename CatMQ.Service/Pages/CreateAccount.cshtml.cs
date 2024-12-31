using CatMQ.Service.Models.Data;
using CatMQ.Service.Models.Page;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace CatMQ.Service.Pages
{
    [Authorize]
    public class CreateAccountModel(ILogger<CreateAccountModel> logger) : BasePageModel
    {
        private readonly ILogger<CreateAccountModel> _logger = logger;

        [BindProperty]
        public Account Account { get; set; } = new();

        public List<TimeZoneItem> TimeZones { get; set; } = new();

        [BindProperty]
        public string? Password { get; set; }


        #region Confirm Action.

        [BindProperty]
        public string? UserSelection { get; set; }

        #endregion

        public IActionResult OnPost()
        {
            try
            {
                if (string.IsNullOrEmpty(Password))
                {
                    ModelState.AddModelError(nameof(Password), "Password cannot be empty.");
                }
                else
                {
                    if (!Utility.IsPasswordComplex(Password, out var errorMessage))
                    {
                        ModelState.AddModelError(nameof(Password), errorMessage);
                    }
                }

                if (ModelState.IsValid)
                {
                    Account.Id = Guid.NewGuid();

                    var accounts = Configs.GetAccounts();
                    accounts.Add(Account);
                    Configs.PutAccounts(accounts);

                    return Redirect($"/account/{Account.Id}?SuccessMessage=Created");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, MethodBase.GetCurrentMethod()?.Name ?? string.Empty);
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

                //Account = new();
                //  ?? throw new Exception("Account was not found.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, MethodBase.GetCurrentMethod()?.Name ?? string.Empty);
                ErrorMessage = ex.Message;
            }
        }
    }
}
