using CatMQ.Service.Models.Page;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

                    var account = accounts.Where(o => o.Id.Equals(AccountId)).Single()
                        .ApiKeys.RemoveAll(o => o.Id == ApiKeyId);

                    Configs.PutAccounts(accounts);
                }
                else
                {
                    return Redirect(RedirectURL);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex.Message);
                ErrorMessage = ex.Message;
            }

            return Page();
        }
    }
}
