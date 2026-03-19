using CatMQ.Service.Models.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace CatMQ.Service.Pages
{
    [AllowAnonymous]
    public class LoginModel(ILogger<LoginModel> logger)
        : PageModel
    {
        [BindProperty]
        public string? Username { get; set; }

        [BindProperty]
        public string? Password { get; set; }

        public bool IsDefaultPassword { get; set; } = false;
        public string? ErrorMessage { get; set; }

        public void OnGet()
        {
            try
            {
                if (!Configs.Exists(Configs.FileType.Accounts))
                {
                    //Create a default accounts file with a default account.
                    var defaultCredentials = new List<Account>
                    {
                        new Account
                        {
                            Id = Guid.NewGuid(),
                            Username = "admin",
                            Description = "default account",
                            PasswordHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("password"))).ToLower()
                        }
                    };
                    Configs.PutAccounts(defaultCredentials);
                }

                var accounts = Configs.GetAccounts();

                IsDefaultPassword = accounts.Any(o => o.Username.Equals("admin", StringComparison.OrdinalIgnoreCase)
                    && o.PasswordHash?.Equals(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("password"))), StringComparison.CurrentCultureIgnoreCase) == true);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during login");
                ErrorMessage = ex.Message;
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            try
            {
                var accounts = Configs.GetAccounts();

                var account = accounts.FirstOrDefault(o => o.Username.Equals(Username, StringComparison.OrdinalIgnoreCase)
                                && o.PasswordHash == Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Password ?? string.Empty))).ToLower());

                if (account != null)
                {
                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.Name, account.Username),
                        new Claim(ClaimTypes.Sid, account.Id.ToString())
                    };

                    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                    var principal = new ClaimsPrincipal(identity);

                    await HttpContext.SignInAsync("CookieAuth", principal);

                    return RedirectToPage("/Index");
                }

                ErrorMessage = "Invalid username or password";
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during login");
                ErrorMessage = ex.Message;
            }
            return Page();
        }

        public async Task<IActionResult> OnGetLogoutAsync()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToPage("/Login");
        }
    }
}
