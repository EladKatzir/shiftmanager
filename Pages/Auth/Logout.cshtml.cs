using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Memory;

namespace ShiftManager.Pages.Auth;

[AllowAnonymous]
public class LogoutModel : PageModel
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<LogoutModel> _logger;

    public bool LoggedOut { get; set; } = false;

    public LogoutModel(IMemoryCache cache, ILogger<LogoutModel> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (User?.Identity?.IsAuthenticated == true)
        {
            var authMethod = User.FindFirst("AuthMethod")?.Value;

            // If Griffin auth, clear Griffin-specific data
            if (authMethod == "Griffin")
            {
                // Clear Griffin token cookie
                Response.Cookies.Delete("griffin.token");

                // Clear cache entry using stored token hash (HIGH-007: raw token no longer in cookie)
                var tokenHash = User.FindFirst("Griffin:TokenHash")?.Value;
                if (!string.IsNullOrEmpty(tokenHash))
                {
                    var cacheKey = $"griffin_claims_{tokenHash}";
                    _cache.Remove(cacheKey);
                }

                _logger.LogInformation("User {UserId} logged out (Griffin ADFS)",
                    User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
            }

            // Clear ASP.NET auth cookie
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        LoggedOut = true;
        return Page();
    }
}
