using CatMQ.Service.Models.Page;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace CatMQ.Service.Pages
{
    [Authorize]
    public class DeleteApiKeyModel(ILogger<DeleteApiKeyModel> logger) : BasePageModel
    {
        private readonly ILogger<DeleteApiKeyModel> _logger = logger;

        public string? RedirectURL { get; set; }

        [BindProperty]
        public Guid? ApiKeyId { get; set; }

        [BindProperty]
        public Guid? AccountId { get; set; }

        [BindProperty]
        public string? UserSelection { get; set; }

        public IActionResult OnPost()
        {
            RedirectURL = $"/Account/{AccountId}";

            try
            {
                if (UserSelection?.Equals("true") == true)
                {
                    var accounts = Configs.GetAccounts();

                    var account = Configs.GetAccounts().Where(o => o.Id.Equals(AccountId)).FirstOrDefault()
                        ?? throw new Exception("Account was not found.");

                    account.ApiKeys.RemoveAll(o => o.Id == ApiKeyId);

                    Configs.PutAccounts(accounts);
                }
                else
                {
                    return Redirect(RedirectURL);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, MethodBase.GetCurrentMethod()?.Name ?? string.Empty);
                ErrorMessage = ex.Message;
            }

            return Page();
        }
    }
}
