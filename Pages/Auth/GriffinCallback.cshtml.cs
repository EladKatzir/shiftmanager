using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Services;

namespace ShiftManager.Pages.Auth;

[AllowAnonymous]
public class GriffinCallbackModel : PageModel
{
    private readonly IGriffinService _griffinService;
    private readonly IGriffinConfigService _griffinConfigService;
    private readonly ISecurityLogger _securityLogger;
    private readonly ILogger<GriffinCallbackModel> _logger;

    public string? Error { get; set; }
    public string? PendingMessage { get; set; }

    public GriffinCallbackModel(
        IGriffinService griffinService,
        IGriffinConfigService griffinConfigService,
        ISecurityLogger securityLogger,
        ILogger<GriffinCallbackModel> logger)
    {
        _griffinService = griffinService;
        _griffinConfigService = griffinConfigService;
        _securityLogger = securityLogger;
        _logger = logger;
    }

    public async Task<IActionResult> OnGetAsync(string? token, string? returnUrl = null)
    {
        // 1. Validate token parameter
        if (string.IsNullOrEmpty(token))
        {
            Error = "Missing authentication token.";
            _logger.LogWarning("Griffin callback invoked without token parameter");
            return Page();
        }

        // 2. Load Griffin config
        var griffinConfig = await _griffinConfigService.GetGriffinConfigAsync();
        if (griffinConfig?.Enabled != true)
        {
            Error = "Griffin ADFS authentication is not enabled.";
            _logger.LogWarning("Griffin callback invoked but Griffin is disabled");
            return Page();
        }

        // 3. Authenticate user
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        ClaimsPrincipal? principal;

        try
        {
            principal = await _griffinService.AuthenticateUserAsync(token, griffinConfig, ipAddress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Griffin authentication failed in callback");
            Error = "Authentication failed. Please try again.";
            _securityLogger.LogAuthenticationFailure("Griffin ADFS", ipAddress, ex.Message);
            return Page();
        }

        if (principal == null)
        {
            // Check if user is pending approval
            var claims = await _griffinService.ValidateAndGetClaimsAsync(
                token, griffinConfig.BaseUrl!, griffinConfig.TimeoutSeconds);

            if (claims != null)
            {
                PendingMessage = "Your account is pending administrator approval.";
                _logger.LogInformation("Griffin user {Email} pending approval", claims.UPN);
                return Page();
            }

            Error = "Authentication failed. Please try again.";
            _securityLogger.LogAuthenticationFailure("Griffin ADFS", ipAddress, "Invalid token or user not found");
            return Page();
        }

        // 4. Set Griffin token cookie
        Response.Cookies.Append("griffin.token", token, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddHours(8),
            Path = "/"
        });

        // 5. Sign in with ASP.NET Core Identity (for compatibility)
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            });

        // 6. Log success
        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = principal.FindFirst(ClaimTypes.Email)?.Value;
        var role = principal.FindFirst(ClaimTypes.Role)?.Value;
        _securityLogger.LogAuthenticationSuccess(int.Parse(userId!), email!, role!, ipAddress);

        // 7. Redirect
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToPage("/Home/Index");
    }
}
