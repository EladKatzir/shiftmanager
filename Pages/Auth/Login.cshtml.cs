using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
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

[AllowAnonymous]
public class LoginModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<LoginModel> _logger;
    private readonly IRateLimitingService _rateLimiting;
    private readonly IValidationService _validation;
    private readonly IGriffinConfigService _griffinConfigService;
    private readonly IGriffinService _griffinService;
    private readonly IHierarchyService _hierarchyService;

    public LoginModel(
        AppDbContext db,
        ILogger<LoginModel> logger,
        IStringLocalizer<SharedResources> localizer,
        IRateLimitingService rateLimiting,
        IValidationService validation,
        IGriffinConfigService griffinConfigService,
        IGriffinService griffinService,
        IHierarchyService hierarchyService)
        : base(localizer)
    {
        _db = db;
        _logger = logger;
        _rateLimiting = rateLimiting;
        _validation = validation;
        _griffinConfigService = griffinConfigService;
        _griffinService = griffinService;
        _hierarchyService = hierarchyService;
    }

    [BindProperty] public string Email { get; set; } = string.Empty;
    [BindProperty] public string Password { get; set; } = string.Empty;
    public bool ShowAuthPrompt { get; set; }
    public bool ShowGriffinButton { get; set; }
    public bool ShowGriffinUnavailableMessage { get; set; }
    public string? ReturnUrl { get; set; }

    // Account lockout warning properties
    public int? FailedAttemptsCount { get; set; }
    public bool ShowLockoutWarning => FailedAttemptsCount.HasValue && FailedAttemptsCount.Value >= 3;
    public bool IsAccountLocked { get; set; }
    public int? LockoutMinutesRemaining { get; set; }

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
            // ✅ SECURITY FIX: Rate limiting — per-IP AND per-account
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var ipRateLimitKey = $"login:ip:{ipAddress}";

            if (!_rateLimiting.IsAllowed(ipRateLimitKey, 10, 15))
            {
                _logger.LogWarning("Rate limit exceeded for login from IP: {IP}", ipAddress);
                Error = _localizer["Error_Login_RateLimitExceeded"];
                return Page();
            }

            // Per-account rate limiting (protects against multi-IP attacks on single account)
            var normalizedEmail = Email?.Trim().ToLowerInvariant() ?? "";
            if (!string.IsNullOrEmpty(normalizedEmail))
            {
                var accountRateLimitKey = $"login:account:{normalizedEmail}";
                if (!_rateLimiting.IsAllowed(accountRateLimitKey, 15, 15))
                {
                    _logger.LogWarning("Per-account rate limit exceeded for {Email} from IP {IP} (possible multi-IP attack)",
                        Services.PiiMasker.MaskEmail(normalizedEmail), ipAddress);
                    Error = _localizer["Error_Login_RateLimitExceeded"];
                    return Page();
                }
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

            // SECURITY-AUDITED: SAFE — login must search across all companies to authenticate users
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
                    _logger.LogWarning("Login attempt for locked account: {Email}. Lockout ends in {Minutes} minutes", ShiftManager.Services.PiiMasker.MaskEmail(Email), remainingMinutes);

                    // Set lockout properties for UI display
                    IsAccountLocked = true;
                    LockoutMinutesRemaining = remainingMinutes;
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

                        _logger.LogWarning("Account locked for {Email} after {Attempts} failed attempts", ShiftManager.Services.PiiMasker.MaskEmail(Email), user.FailedLoginAttempts);

                        // Set lockout properties for UI display
                        IsAccountLocked = true;
                        LockoutMinutesRemaining = 3;
                        Error = _localizer["Error_Login_AccountLockedAfterAttempts"];
                        return Page();
                    }

                    await _db.SaveChangesAsync();
                    _logger.LogWarning("Login failed for {Email} (attempt {Attempt}/10)", ShiftManager.Services.PiiMasker.MaskEmail(Email), user.FailedLoginAttempts);

                    // Set failed attempts count for warning display (only if user exists)
                    FailedAttemptsCount = user.FailedLoginAttempts;
                }
                else
                {
                    _logger.LogWarning("Login failed for {Email} (user not found)", ShiftManager.Services.PiiMasker.MaskEmail(Email));
                }

                Error = _localizer["Error_Login_InvalidCredentials"];
                return Page();
            }

            // ✅ SECURITY FIX: Reset failed attempts and rate limit on successful login
            user.FailedLoginAttempts = 0;
            user.LockoutEnd = null;
            await _db.SaveChangesAsync();

            // Reset rate limits for this IP and account after successful login
            _rateLimiting.Reset(ipRateLimitKey);
            _rateLimiting.Reset($"login:account:{normalizedEmail}");

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.DisplayName),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
                new Claim("CompanyId", user.CompanyId.ToString())
            };

            // v3.0 Organizational Hierarchy claims
            var hierarchyContext = await _hierarchyService.GetUserHierarchyContextAsync(user.Id);
            if (hierarchyContext != null)
            {
                claims.Add(new Claim("MoleculeId", hierarchyContext.Path.Molecule.Id.ToString()));
                claims.Add(new Claim("AreaId", hierarchyContext.Path.Area.Id.ToString()));
                claims.Add(new Claim("ProjectId", hierarchyContext.Path.Project.Id.ToString()));
                claims.Add(new Claim("IsWorkforce", hierarchyContext.IsWorkforce.ToString()));
                claims.Add(new Claim("IsTech", hierarchyContext.IsTech.ToString()));

                if (hierarchyContext.JobType != null)
                    claims.Add(new Claim("JobTypeId", hierarchyContext.JobType.Id.ToString()));

                if (hierarchyContext.Path.Department != null)
                    claims.Add(new Claim("DepartmentId", hierarchyContext.Path.Department.Id.ToString()));
            }

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

            _logger.LogInformation("User {UserId} ({Email}) signed in successfully. Role={Role}", user.Id, ShiftManager.Services.PiiMasker.MaskEmail(user.Email), user.Role);

            // A-07: Force redirect to password change page if MustChangePassword flag is set
            if (user.MustChangePassword)
            {
                _logger.LogInformation("User {UserId} must change password — redirecting to ForgotPassword", user.Id);
                return RedirectToPage("/Auth/ForgotPassword");
            }

            // B-05: Redirect new users to onboarding wizard on first login
            if (!user.HasCompletedOnboarding)
            {
                _logger.LogInformation("User {UserId} has not completed onboarding — redirecting to wizard", user.Id);
                return RedirectToPage("/My/Onboarding");
            }

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

        // ✅ CRITICAL FIX: Validate URLs have proper scheme (http:// or https://)
        if (!Uri.TryCreate(griffinConfig.BaseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            _logger.LogError("CRITICAL: Griffin BaseUrl is missing scheme or invalid: '{BaseUrl}'", griffinConfig.BaseUrl);
            _logger.LogError("BaseUrl must start with http:// or https://. Current value will cause 404 redirect error.");
            Error = "Griffin ADFS configuration error: Base URL must start with http:// or https://. Please contact your administrator to fix this in /Owner/GriffinConfig.";
            ReturnUrl = returnUrl ?? "/";
            ShowGriffinButton = false;
            ShowGriffinUnavailableMessage = true;
            await OnGetAsync(returnUrl: returnUrl);
            return Page();
        }

        if (!Uri.TryCreate(griffinConfig.TokenConsumerUrl, UriKind.Absolute, out var callbackUri) ||
            (callbackUri.Scheme != Uri.UriSchemeHttp && callbackUri.Scheme != Uri.UriSchemeHttps))
        {
            _logger.LogError("CRITICAL: Griffin TokenConsumerUrl is missing scheme or invalid: '{TokenConsumerUrl}'", griffinConfig.TokenConsumerUrl);
            _logger.LogError("TokenConsumerUrl must start with http:// or https://. Current value will cause authentication failure.");
            Error = "Griffin ADFS configuration error: Callback URL must start with http:// or https://. Please contact your administrator to fix this in /Owner/GriffinConfig.";
            ReturnUrl = returnUrl ?? "/";
            ShowGriffinButton = false;
            ShowGriffinUnavailableMessage = true;
            await OnGetAsync(returnUrl: returnUrl);
            return Page();
        }

        // ✅ FIX: Use configured TokenConsumerUrl from database (not Request.Scheme/Host)
        var callbackUrl = griffinConfig.TokenConsumerUrl;

        // Append returnUrl as query parameter if present
        if (!string.IsNullOrEmpty(returnUrl))
        {
            // Triple-encode the returnUrl to survive multiple decoding layers:
            // 1st layer: Griffin's HTTP query parameter parsing
            // 2nd layer: Griffin's internal processing/redirect
            // 3rd layer: Our ASP.NET Core query parameter parsing
            // This matches the Doof pattern of triple-encoding the destination path
            var encodedReturn = Uri.EscapeDataString(returnUrl);
            var encodedTwice = Uri.EscapeDataString(encodedReturn);
            var encodedThrice = Uri.EscapeDataString(encodedTwice);

            var separator = callbackUrl.Contains('?') ? '&' : '?';
            callbackUrl += $"{separator}returnUrl={encodedThrice}";

            _logger.LogDebug("Triple-encoding returnUrl:");
            _logger.LogDebug("  - Original returnUrl: {Original}", returnUrl);
            _logger.LogDebug("  - After 1st encode: {Encoded1}", encodedReturn);
            _logger.LogDebug("  - After 2nd encode: {Encoded2}", encodedTwice);
            _logger.LogDebug("  - After 3rd encode: {Encoded3}", encodedThrice);
        }

        _logger.LogInformation("=== GRIFFIN ADFS REDIRECT DEBUG ===");
        _logger.LogInformation("Config from database:");
        _logger.LogInformation("  - BaseUrl: {BaseUrl}", griffinConfig.BaseUrl);
        _logger.LogInformation("  - TokenConsumerUrl: {TokenConsumerUrl}", griffinConfig.TokenConsumerUrl);
        _logger.LogInformation("  - CallbackUrl (with triple-encoded returnUrl): {CallbackUrl}", callbackUrl);

        // Build authentication URL
        var authUrl = _griffinService.BuildAuthenticationUrl(griffinConfig.BaseUrl, callbackUrl);

        _logger.LogInformation("Generated authentication URL: {AuthUrl}", authUrl);

        // ✅ CRITICAL FIX: Validate the generated URL is absolute before redirecting
        if (!Uri.TryCreate(authUrl, UriKind.Absolute, out var authUri))
        {
            _logger.LogError("CRITICAL: Generated auth URL is NOT absolute: '{AuthUrl}'", authUrl);
            _logger.LogError("This will cause ASP.NET to treat it as a relative path, resulting in 404 error.");
            _logger.LogError("Check that BaseUrl starts with http:// or https://");
            Error = "Griffin ADFS configuration error: Generated authentication URL is invalid. Please contact your administrator.";
            ReturnUrl = returnUrl ?? "/";
            ShowGriffinButton = false;
            ShowGriffinUnavailableMessage = true;
            await OnGetAsync(returnUrl: returnUrl);
            return Page();
        }

        _logger.LogInformation("URL validation:");
        _logger.LogInformation("  - Is Absolute: YES ✓");
        _logger.LogInformation("  - Scheme: {Scheme}", authUri.Scheme);
        _logger.LogInformation("  - Host: {Host}", authUri.Host);
        _logger.LogInformation("  - Path: {Path}", authUri.AbsolutePath);
        _logger.LogInformation("  - Query: {Query}", authUri.Query.Length > 100 ? authUri.Query.Substring(0, 100) + "..." : authUri.Query);
        _logger.LogInformation("Redirecting browser to Griffin ADFS...");
        _logger.LogInformation("=== END DEBUG ===");

        // Redirect to Griffin
        return Redirect(authUrl);
    }
}
