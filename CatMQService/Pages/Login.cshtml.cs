using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Newtonsoft.Json;
using NTDLS.CatMQServer;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace CatMQService.Pages
{
    [AllowAnonymous]
    public class LoginModel(ILogger<LoginModel> logger, CMqServer mqServer) : PageModel
    {
        private readonly ILogger<LoginModel> _logger = logger;

        [BindProperty]
        public string? Username { get; set; }

        [BindProperty]
        public string? Password { get; set; }

        public string? ErrorMessage { get; set; }

        public bool IsDefaultPassword { get; set; } = false;


        public void OnGet()
        {
            try
            {
                var serverConfiguration = mqServer.GetConfiguration();
                var usersFile = Path.Join(serverConfiguration.PersistencePath, "credentials.json");
                if (System.IO.File.Exists(usersFile) == false)
                {
                    var defaultCredentials = new List<UserCredential>
                    {
                        new UserCredential
                        {
                            Username = "admin",
                            PasswordHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("password"))).ToLower()
                        }
                    };
                    System.IO.File.WriteAllText(usersFile, JsonConvert.SerializeObject(defaultCredentials));
                }

                var credentials = JsonConvert.DeserializeObject<List<UserCredential>>(System.IO.File.ReadAllText(usersFile)) ?? new();

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
                var serverConfiguration = mqServer.GetConfiguration();

                var usersFile = Path.Join(serverConfiguration.PersistencePath, "credentials.json");
                var credentials = JsonConvert.DeserializeObject<List<UserCredential>>(System.IO.File.ReadAllText(usersFile)) ?? new();
                var credential = credentials.FirstOrDefault(o => o.Username.Equals(Username, StringComparison.OrdinalIgnoreCase)
                                && o.PasswordHash == Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Password ?? string.Empty))).ToLower());

                if (credential != null)
                {
                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.Name, credential.Username)
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
