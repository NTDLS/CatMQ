using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CatMQ.Service.Models.Page
{
    public class BasePageModel : PageModel
    {
        public string? SuccessMessage { get; set; }
        public string? WarningMessage { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
