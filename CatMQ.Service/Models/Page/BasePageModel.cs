using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CatMQ.Service.Models.Page
{
    public class BasePageModel : PageModel
    {
        [BindProperty(SupportsGet = true)]

        public string? SuccessMessage { get; set; }
        [BindProperty(SupportsGet = true)]

        public string? WarningMessage { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? ErrorMessage { get; set; }
    }
}
