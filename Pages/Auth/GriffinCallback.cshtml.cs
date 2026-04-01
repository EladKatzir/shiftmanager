using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Pages.Auth;

[AllowAnonymous]
public class GriffinCallbackModel : LocalizedPageModel
{
    private readonly IGriffinService _griffinService;
    private readonly IGriffinConfigService _griffinConfigService;
    private readonly ISecurityLogger _securityLogger;
    private readonly ILogger<GriffinCallbackModel> _logger;
    // SECURITY-AUDITED: SAFE — IgnoreQueryFilters used only for cross-company deactivated-user check
    // before tenant context is established; scoped by explicit email comparison
    private readonly AppDbContext _db;

    public string? PendingMessage { get; set; }

    public GriffinCallbackModel(
        IStringLocalizer<SharedResources> localizer,
        IGriffinService griffinService,
        IGriffinConfigService griffinConfigService,
        ISecurityLogger securityLogger,
        ILogger<GriffinCallbackModel> logger,
        AppDbContext db)
        : base(localizer)
    {
        _griffinService = griffinService;
        _griffinConfigService = griffinConfigService;
        _securityLogger = securityLogger;
        _logger = logger;
        _db = db;
    }

    public async Task<IActionResult> OnGetAsync(
        [FromQuery(Name = "token")] string? token,
        [FromQuery(Name = "HashedToken")] string? hashedToken,
        [FromQuery(Name = "hasedToken")] string? hasedToken,
        string? returnUrl = null)
    {
        // 1. Validate token parameter — Griffin may send as "token", "HashedToken", or "hasedToken"
        var effectiveToken = token ?? hashedToken ?? hasedToken;
        if (string.IsNullOrEmpty(effectiveToken))
        {
            Error = _localizer["Error_MissingAuthToken"].Value;
            _logger.LogWarning("Griffin callback invoked without token parameter. Query: {Query}",
                Request.QueryString.Value);
            return Page();
        }
        token = effectiveToken;

        // 2. Load Griffin config
        var griffinConfig = await _griffinConfigService.GetGriffinConfigAsync();
        if (griffinConfig?.Enabled != true)
        {
            Error = _localizer["Error_GriffinNotEnabled"].Value;
            _logger.LogWarning("Griffin callback invoked but Griffin is disabled");
            return Page();
        }

        // 3. Exchange hashed token (A) for real token (B)
        var realToken = await _griffinService.ExchangeTokenAsync(
            token, griffinConfig.BaseUrl!, griffinConfig.TimeoutSeconds);
        if (string.IsNullOrEmpty(realToken))
        {
            Error = _localizer["Error_GriffinTokenExchangeFailed"].Value;
            _logger.LogWarning("Griffin token exchange failed for callback token");
            return Page();
        }
        token = realToken; // All subsequent operations use token B

        // 4. Authenticate user
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        ClaimsPrincipal? principal;

        try
        {
            principal = await _griffinService.AuthenticateUserAsync(token, griffinConfig, ipAddress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Griffin authentication failed in callback");
            Error = _localizer["Error_AuthenticationFailed"].Value;
            _securityLogger.LogAuthenticationFailure("Griffin ADFS", ipAddress, ex.Message);
            return Page();
        }

        if (principal == null)
        {
            // Token was valid (exchange succeeded) — check if user exists in Griffin but not in ShiftManager
            var claims = await _griffinService.ValidateAndGetClaimsAsync(
                token, griffinConfig.BaseUrl!, griffinConfig.TimeoutSeconds);

            if (claims != null)
            {
                // Check if user already exists (active or not) — don't redirect to signup
                // Active user hitting this path = transient Griffin failure; Inactive = deactivated account
                // SECURITY-AUDITED: SAFE — cross-company lookup needed before tenant context established
                var existingUser = await _db.Users.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(u => u.Email.ToLower() == claims.EmailAddress.ToLower());
                if (existingUser != null)
                {
                    Error = existingUser.IsActive
                        ? _localizer["Error_AuthenticationFailed"].Value   // transient Griffin failure
                        : _localizer["Error_AccountDeactivated"].Value;    // deactivated account
                    _logger.LogWarning("Griffin user {Email} exists (IsActive={IsActive}) but AuthenticateUserAsync returned null",
                        claims.EmailAddress, existingUser.IsActive);
                    _securityLogger.LogAuthenticationFailure(claims.EmailAddress, ipAddress,
                        existingUser.IsActive ? "Auth failed for existing user" : "Account deactivated");
                    return Page();
                }

                // Valid Griffin user but not in ShiftManager → redirect to signup
                // Store token B in cookie (HttpOnly, short TTL) so GriffinSignup can re-validate
                // without exposing the bearer token in TempData (which uses client-side cookies)
                Response.Cookies.Append("griffin.token", token, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = Request.IsHttps,
                    SameSite = SameSiteMode.Lax,
                    Expires = DateTimeOffset.UtcNow.AddMinutes(15),
                    Path = "/"
                });

                // Pass non-sensitive display data via TempData (no token!)
                TempData["GriffinEmail"] = claims.EmailAddress;
                TempData["GriffinDisplayName"] = claims.DisplayName;
                TempData["GriffinUniqueID"] = claims.UniqueID;

                _logger.LogInformation("Griffin user {Email} not found in ShiftManager, redirecting to signup", claims.EmailAddress);
                return RedirectToPage("/Auth/GriffinSignup");
            }

            Error = _localizer["Error_AuthenticationFailed"].Value;
            _securityLogger.LogAuthenticationFailure("Griffin ADFS", ipAddress, "Invalid token or user not found");
            return Page();
        }

        // 5. Set Griffin token cookie
        Response.Cookies.Append("griffin.token", token, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddHours(8),
            Path = "/"
        });

        // 6. Sign in with ASP.NET Core Identity (for compatibility)
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            });

        // 7. Log success
        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = principal.FindFirst(ClaimTypes.Email)?.Value;
        var role = principal.FindFirst(ClaimTypes.Role)?.Value;
        if (int.TryParse(userId, out var parsedUid))
            _securityLogger.LogAuthenticationSuccess(parsedUid, email ?? "", role ?? "", ipAddress);

        // 8. Redirect — check query string first, then cookie fallback
        if (string.IsNullOrEmpty(returnUrl))
        {
            // Read returnUrl from cookie (set by Login page before Griffin redirect)
            returnUrl = Request.Cookies["griffin.returnUrl"];
            if (!string.IsNullOrEmpty(returnUrl))
            {
                _logger.LogDebug("Read returnUrl from cookie: {ReturnUrl}", returnUrl);
            }
        }

        // Clean up the returnUrl cookie
        Response.Cookies.Delete("griffin.returnUrl", new CookieOptions { Path = "/Auth" });

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToPage("/Home/Index");
    }
}
