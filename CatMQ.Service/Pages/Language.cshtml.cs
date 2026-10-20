using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Builder;
using System.Globalization;

namespace CatMQ.Service.Pages
{
    [AllowAnonymous]
    public class LanguageModel(IOptions<RequestLocalizationOptions> localizationOptions)
        : PageModel
    {
        public List<CultureInfo> Languages { get; private set; } = new();

        /// <summary>
        /// Where to go once the language has been chosen, normally the page that the user came from.
        /// </summary>
        public string ReturnUrl { get; private set; } = "/";

        public void OnGet(string? returnUrl = null)
        {
            var referrer = Request.Headers.Referer.ToString();
            ReturnUrl = SafeReturnUrl(returnUrl ?? referrer);

            Languages = (localizationOptions.Value.SupportedUICultures ?? new List<CultureInfo>())
                .OrderBy(o => o.EnglishName, StringComparer.Create(CultureInfo.CurrentUICulture, ignoreCase: true))
                .ToList();
        }

        public IActionResult OnGetSet(string? culture, string? returnUrl)
        {
            var supported = localizationOptions.Value.SupportedUICultures ?? new List<CultureInfo>();

            if (string.IsNullOrWhiteSpace(culture)
                || supported.Any(o => o.Name.Equals(culture, StringComparison.OrdinalIgnoreCase)) == false)
            {
                return BadRequest();
            }

            Response.Cookies.Append(CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax,
                    Secure = Request.IsHttps,
                    HttpOnly = false
                });

            return Redirect(SafeReturnUrl(returnUrl));
        }

        /// <summary>
        /// Only returns to a page on this site, so that the language links cannot be used to redirect elsewhere.
        /// </summary>
        private string SafeReturnUrl(string? returnUrl)
        {
            if (string.IsNullOrWhiteSpace(returnUrl))
            {
                return "/";
            }

            if (Uri.TryCreate(returnUrl, UriKind.Absolute, out var absolute))
            {
                if (absolute.Host.Equals(Request.Host.Host, StringComparison.OrdinalIgnoreCase))
                {
                    returnUrl = absolute.PathAndQuery;
                }
            }

            return Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
        }
    }
}
