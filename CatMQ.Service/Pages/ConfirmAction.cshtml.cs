using CatMQ.Service.Models.Page;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace CatMQ.Service.Pages
{
    [Authorize]
    public class ConfirmActionModel(ILogger<ConfirmActionModel> logger) : BasePageModel
    {
        private readonly ILogger<ConfirmActionModel> _logger = logger;


        [BindProperty(SupportsGet = true)]
        public string AspHandler { get; set; } = string.Empty;

        [BindProperty(SupportsGet = true)]
        public string PostBackTo { get; set; } = string.Empty;

        [BindProperty(SupportsGet = true)]
        public string Message { get; set; } = string.Empty;

        [BindProperty(SupportsGet = true)]
        public string Style { get; set; } = string.Empty;

        public IActionResult OnPostSaveAccount()
        {
            try
            {
                if (ModelState.IsValid)
                {
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, MethodBase.GetCurrentMethod()?.Name ?? string.Empty);
                ErrorMessage = ex.Message;
            }

            return Page();
        }

        public void OnGet()
        {
            try
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, MethodBase.GetCurrentMethod()?.Name ?? string.Empty);
                ErrorMessage = ex.Message;
            }
        }
    }
}
