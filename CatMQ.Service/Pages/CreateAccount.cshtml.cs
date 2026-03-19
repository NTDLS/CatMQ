using CatMQ.Service.Models.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CatMQ.Service.Pages
{
    [Authorize]
    public class CreateAccountModel(ILogger<CreateAccountModel> logger)
        : PageModel
    {

        [BindProperty]
        public Account Account { get; set; } = new();

        public List<TimeZoneItem> TimeZones { get; set; } = new();

        [BindProperty]
        public string? Password { get; set; }
        public string? ErrorMessage { get; set; }


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
                logger.LogError(ex, "Error creating account");
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
                logger.LogError(ex, "Error getting account for creation");
                ErrorMessage = ex.Message;
            }
        }
    }
}
