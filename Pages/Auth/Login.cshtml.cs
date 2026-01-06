using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;
using Microsoft.Extensions.Logging;

namespace ShiftManager.Pages.Auth;

public class LoginModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<LoginModel> _logger;
    private readonly IRateLimitingService _rateLimiting;
    private readonly IValidationService _validation;
    private readonly IGriffinConfigService _griffinConfigService;
    private readonly IGriffinService _griffinService;

    public LoginModel(
        AppDbContext db,
        ILogger<LoginModel> logger,
        IStringLocalizer<SharedResources> localizer,
        IRateLimitingService rateLimiting,
        IValidationService validation,
        IGriffinConfigService griffinConfigService,
        IGriffinService griffinService)
        : base(localizer)
    {
        _db = db;
        _logger = logger;
        _rateLimiting = rateLimiting;
        _validation = validation;
        _griffinConfigService = griffinConfigService;
        _griffinService = griffinService;
    }

    [BindProperty] public string Email { get; set; } = string.Empty;
    [BindProperty] public string Password { get; set; } = string.Empty;
    public bool ShowAuthPrompt { get; set; }
    public bool ShowGriffinButton { get; set; }
    public bool ShowGriffinUnavailableMessage { get; set; }
    public string? ReturnUrl { get; set; }

    public async Task OnGetAsync(string? reason = null, string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? "/Home/Index";

        // ✅ PHASE 18: Show auth required prompt if user was redirected due to unauthorized access
        ShowAuthPrompt = reason == "authRequired";

        // ✅ FIX: Improved Griffin availability check with detailed logging
        var griffinConfig = await _griffinConfigService.GetGriffinConfigAsync();

        if (griffinConfig == null)
        {
            // Not configured at all (database table missing or appsettings disabled)
            ShowGriffinButton = false;
            ShowGriffinUnavailableMessage = false;
            _logger.LogDebug("Griffin config not found (database table may be missing or appsettings.json has Enabled=false)");
        }
        else if (!griffinConfig.Enabled)
        {
            // Configured but explicitly disabled
            ShowGriffinButton = false;
            ShowGriffinUnavailableMessage = false;
            _logger.LogDebug("Griffin config exists but Enabled=false for company {CompanyId}", griffinConfig.CompanyId);
        }
        else if (string.IsNullOrWhiteSpace(griffinConfig.BaseUrl) ||
                 string.IsNullOrWhiteSpace(griffinConfig.TokenConsumerUrl))
        {
            // Enabled but incomplete configuration
            ShowGriffinButton = false;
            ShowGriffinUnavailableMessage = true;
            _logger.LogWarning("Griffin enabled but configuration incomplete: BaseUrl={BaseUrl}, TokenConsumerUrl={TokenConsumerUrl}",
                griffinConfig.BaseUrl ?? "(null)",
                griffinConfig.TokenConsumerUrl ?? "(null)");
        }
        else
        {
            // Fully configured - test connection
            _logger.LogDebug("Testing Griffin connection to {BaseUrl}", griffinConfig.BaseUrl);

            var result = await _griffinConfigService.TestConnectionAsync(
                griffinConfig.BaseUrl, griffinConfig.TimeoutSeconds);

            ShowGriffinButton = result.Success;
            ShowGriffinUnavailableMessage = !result.Success;

            if (result.Success)
            {
                _logger.LogDebug("Griffin ADFS is available and configured correctly");
            }
            else
            {
                _logger.LogWarning("Griffin ADFS connection test failed for {BaseUrl} (timeout: {Timeout}s)",
                    griffinConfig.BaseUrl, griffinConfig.TimeoutSeconds);
            }
        }

        // ✅ SUB-PHASE 18.14: Prevent browser caching to ensure link renders correctly
        Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Expires"] = "0";
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        try
        {
            // ✅ SECURITY FIX: Rate limiting (10 attempts per 15 minutes per IP)
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var rateLimitKey = $"login:{ipAddress}";

            if (!_rateLimiting.IsAllowed(rateLimitKey, 10, 15))
            {
                _logger.LogWarning("Rate limit exceeded for login from IP: {IP}", ipAddress);
                Error = _localizer["Error_Login_RateLimitExceeded"];
                return Page();
            }

            // ✅ SECURITY FIX: Input validation
            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
            {
                Error = _localizer["Error_Login_FieldsRequired"];
                return Page();
            }

            if (Email.Length > 255 || Password.Length > 500)
            {
                _logger.LogWarning("Login attempt with oversized input from IP {IP}", ipAddress);
                Error = _localizer["Error_InvalidInput"];
                return Page();
            }

            // ✅ SECURITY FIX: Proper email format validation with regex
            if (!_validation.IsValidEmail(Email))
            {
                Error = _localizer["Error_InvalidEmailFormat"];
                return Page();
            }

            var user = await _db.Users
                .IgnoreQueryFilters() // Allow login across all companies
                .FirstOrDefaultAsync(u => u.Email == Email && u.IsActive);

            // ✅ SECURITY FIX: Account lockout protection
            if (user != null)
            {
                // Check if account is locked out
                if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTime.UtcNow)
                {
                    var remainingMinutes = (int)(user.LockoutEnd.Value - DateTime.UtcNow).TotalMinutes + 1;
                    _logger.LogWarning("Login attempt for locked account: {Email}. Lockout ends in {Minutes} minutes", Email, remainingMinutes);
                    Error = _localizer["Error_Login_AccountLocked", remainingMinutes];
                    return Page();
                }

                // Record login attempt time
                user.LastLoginAttempt = DateTime.UtcNow;
            }

            // Verify password
            if (user == null || !PasswordHasher.Verify(Password, user.PasswordHash, user.PasswordSalt))
            {
                // ✅ SECURITY FIX: Track failed login attempts
                if (user != null)
                {
                    user.FailedLoginAttempts++;

                    // Lock account after 10 failed attempts
                    if (user.FailedLoginAttempts >= 10)
                    {
                        user.LockoutEnd = DateTime.UtcNow.AddMinutes(3);
                        await _db.SaveChangesAsync();

                        _logger.LogWarning("Account locked for {Email} after {Attempts} failed attempts", Email, user.FailedLoginAttempts);
                        Error = _localizer["Error_Login_AccountLockedAfterAttempts"];
                        return Page();
                    }

                    await _db.SaveChangesAsync();
                    _logger.LogWarning("Login failed for {Email} (attempt {Attempt}/10)", Email, user.FailedLoginAttempts);
                }
                else
                {
                    _logger.LogWarning("Login failed for {Email} (user not found)", Email);
                }

                Error = _localizer["Error_Login_InvalidCredentials"];
                return Page();
            }

            // ✅ SECURITY FIX: Reset failed attempts and rate limit on successful login
            user.FailedLoginAttempts = 0;
            user.LockoutEnd = null;
            await _db.SaveChangesAsync();

            // Reset rate limit for this IP after successful login
            _rateLimiting.Reset(rateLimitKey);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.DisplayName),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
                new Claim("CompanyId", user.CompanyId.ToString())
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

            _logger.LogInformation("User {UserId} ({Email}) signed in successfully. Role={Role}", user.Id, user.Email, user.Role);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            // ✅ PHASE 18: Redirect based on role - Owner uses old home, others use new redesigned home
            if (user.Role == UserRole.Owner)
                return RedirectToPage("/Home/Index");
            else
                return Redirect("/");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception during login for {Email}", Email);
            Error = _localizer["Error_UnexpectedError"];
            return Page();
        }
    }

    public async Task<IActionResult> OnPostGriffinAsync(string? returnUrl = null)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        _logger.LogInformation("Griffin authentication initiated from IP {IP}", ipAddress);

        var griffinConfig = await _griffinConfigService.GetGriffinConfigAsync();

        // Validate Griffin is enabled
        if (griffinConfig?.Enabled != true)
        {
            _logger.LogWarning("Griffin authentication attempt but config not enabled (config null or Enabled=false)");
            Error = _localizer["Error_Login_AdfsNotConfigured"];
            ReturnUrl = returnUrl ?? "/";
            ShowGriffinButton = true;
            ShowGriffinUnavailableMessage = true;
            await OnGetAsync(returnUrl: returnUrl);
            return Page();
        }

        // Validate required configuration fields
        if (string.IsNullOrWhiteSpace(griffinConfig.BaseUrl))
        {
            _logger.LogError("Griffin enabled but BaseUrl is missing");
            Error = _localizer["Error_Login_AdfsNotConfigured"];
            ReturnUrl = returnUrl ?? "/";
            ShowGriffinButton = true;
            ShowGriffinUnavailableMessage = true;
            await OnGetAsync(returnUrl: returnUrl);
            return Page();
        }

        if (string.IsNullOrWhiteSpace(griffinConfig.TokenConsumerUrl))
        {
            _logger.LogError("Griffin enabled but TokenConsumerUrl is missing");
            Error = _localizer["Error_Login_AdfsNotConfigured"];
            ReturnUrl = returnUrl ?? "/";
            ShowGriffinButton = true;
            ShowGriffinUnavailableMessage = true;
            await OnGetAsync(returnUrl: returnUrl);
            return Page();
        }

        // ✅ FIX: Use configured TokenConsumerUrl from database (not Request.Scheme/Host)
        var callbackUrl = griffinConfig.TokenConsumerUrl;

        // Append returnUrl as query parameter if present
        if (!string.IsNullOrEmpty(returnUrl))
        {
            var separator = callbackUrl.Contains('?') ? '&' : '?';
            callbackUrl += $"{separator}returnUrl={Uri.EscapeDataString(returnUrl)}";
        }

        _logger.LogDebug("Using configured callback URL: {CallbackUrl}", callbackUrl);
        _logger.LogDebug("Request context: Scheme={Scheme}, Host={Host}", Request.Scheme, Request.Host);

        // Build authentication URL
        var authUrl = _griffinService.BuildAuthenticationUrl(griffinConfig.BaseUrl, callbackUrl);

        _logger.LogInformation("Redirecting to Griffin ADFS: {BaseUrl}/authentication", griffinConfig.BaseUrl);
        _logger.LogDebug("Full Griffin auth URL (tokenConsumerURL will be double-encoded): {AuthUrl}", authUrl);

        // Redirect to Griffin
        return Redirect(authUrl);
    }
}
