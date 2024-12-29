using CatMQ.Service.Models.Data;
using CatMQ.Service.Models.Page;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace CatMQ.Service.Pages
{
    [AllowAnonymous]
    public class LoginModel(ILogger<LoginModel> logger, ServiceConfiguration serviceConfiguration) : BasePageModel
    {
        private readonly ILogger<LoginModel> _logger = logger;

        [BindProperty]
        public string? Username { get; set; }

        [BindProperty]
        public string? Password { get; set; }

        public bool IsDefaultPassword { get; set; } = false;

        public void OnGet()
        {
            try
            {
                var usersFile = Path.Join(serviceConfiguration.DataPath, "accounts.json");
                if (System.IO.File.Exists(usersFile) == false)
                {
                    //Create a default accounts file with a default account.
                    var defaultCredentials = new List<Account>
                    {
                        new Account
                        {
                            Username = "admin",
                            PasswordHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("password"))).ToLower()
                        }
                    };
                    serviceConfiguration.Write("accounts.json", defaultCredentials);
                }

                var credentials = JsonConvert.DeserializeObject<List<Account>>(System.IO.File.ReadAllText(usersFile)) ?? new();

                IsDefaultPassword = credentials.Any(o => o.Username.Equals("admin", StringComparison.OrdinalIgnoreCase)
                                                && o.PasswordHash == Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("password"))).ToLower());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login");
                ErrorMessage = ex.Message;
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            try
            {
                var accounts = serviceConfiguration.Read<List<Account>>("accounts.json", new());

                var account = accounts.FirstOrDefault(o => o.Username.Equals(Username, StringComparison.OrdinalIgnoreCase)
                                && o.PasswordHash == Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Password ?? string.Empty))).ToLower());

                if (account != null)
                {
                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.Name, account.Username)
                    };

                    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                    var principal = new ClaimsPrincipal(identity);

                    await HttpContext.SignInAsync("CookieAuth", principal);

                    return RedirectToPage("/Index");
                }

                WarningMessage = "Invalid username or password";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login");
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
