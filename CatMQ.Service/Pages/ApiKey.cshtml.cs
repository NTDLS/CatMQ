using CatMQ.Service.Models.Data;
using CatMQ.Service.Models.Page;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CatMQ.Service.Pages
{
    [Authorize]
    public class ApiKeyModel(ILogger<ApiKeyModel> logger) : BasePageModel
    {
        private readonly ILogger<ApiKeyModel> _logger = logger;

        [BindProperty]
        public AccountApiKey AccountApiKey { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public Guid? ApiKeyId { get; set; }

        [BindProperty(SupportsGet = true)]
        public Guid? AccountId { get; set; }


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
                _logger.LogWarning(ex.Message);
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
                _logger.LogWarning(ex.Message);
                ErrorMessage = ex.Message;
            }
        }
    }
}
