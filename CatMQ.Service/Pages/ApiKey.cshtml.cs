using CatMQ.Service.Models.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CatMQ.Service.Pages
{
    [Authorize]
    public class ApiKeyModel(ILogger<ApiKeyModel> logger)
        : PageModel
    {
        [BindProperty]
        public AccountApiKey AccountApiKey { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public Guid? ApiKeyId { get; set; }

        [BindProperty(SupportsGet = true)]
        public Guid? AccountId { get; set; }
        public string? ErrorMessage { get; set; }
        public string? SuccessMessage { get; set; }


        public IActionResult OnPost()
        {
            try
            {
                var accounts = Configs.GetAccounts();
                var account = accounts.Single(o => o.Id == AccountId);

                if (ModelState.IsValid)
                {
                    account.ApiKeys.RemoveAll(o => o.Id == ApiKeyId);
                    AccountApiKey.Id = ApiKeyId;
                    account.ApiKeys.Add(AccountApiKey);

                    Configs.PutAccounts(accounts);

                    SuccessMessage = "Saved!";
                }

                AccountApiKey = account.ApiKeys.Single(o => o.Id == ApiKeyId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error saving API key");
                ErrorMessage = ex.Message;
            }

            return Page();
        }

        public void OnGet()
        {
            try
            {
                var accounts = Configs.GetAccounts();
                var account = accounts.Single(o => o.Id == AccountId);
                AccountApiKey = account.ApiKeys.Single(o => o.Id == ApiKeyId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting API key");
                ErrorMessage = ex.Message;
            }
        }
    }
}
