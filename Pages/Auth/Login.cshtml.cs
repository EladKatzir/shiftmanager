using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Data.SeedData;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;
using Microsoft.Extensions.Logging;

namespace ShiftManager.Pages.Auth;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — anonymous auth flow before tenant context established;
// scoped by explicit email parameter; user lookup for authentication only
[AllowAnonymous]
public partial class LoginModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<LoginModel> _logger;
    private readonly IRateLimitingService _rateLimiting;
    private readonly IValidationService _validation;
    private readonly IGriffinConfigService _griffinConfigService;
    private readonly IGriffinService _griffinService;
    private readonly IFeatureFlagService _featureFlagService;
    private readonly IHierarchyService _hierarchyService;
    private readonly IGrantService _grantService;
    private readonly IRoleService _roleService;

    public LoginModel(
        AppDbContext db,
        ILogger<LoginModel> logger,
        IStringLocalizer<SharedResources> localizer,
        IRateLimitingService rateLimiting,
        IValidationService validation,
        IGriffinConfigService griffinConfigService,
        IGriffinService griffinService,
        IFeatureFlagService featureFlagService,
        IHierarchyService hierarchyService,
        IGrantService grantService,
        IRoleService roleService)
        : base(localizer)
    {
        _db = db;
        _logger = logger;
        _rateLimiting = rateLimiting;
        _validation = validation;
        _griffinConfigService = griffinConfigService;
        _griffinService = griffinService;
        _featureFlagService = featureFlagService;
        _hierarchyService = hierarchyService;
        _grantService = grantService;
        _roleService = roleService;
    }

    [BindProperty] public string Email { get; set; } = string.Empty;
    [BindProperty] public string Password { get; set; } = string.Empty;

    // Set to "true" by the Ctrl+click escape-hatch handler when the regular login form is
    // revealed (only visible when FF_HIDE_REGULAR_LOGIN is enabled). Auditing this lets us
    // distinguish "admin used the escape hatch" from "Griffin was unavailable so the form
    // was always visible." Hidden field; never displayed to the user.
    [BindProperty] public bool EscapeHatchUsed { get; set; }
    public bool ShowAuthPrompt { get; set; }
    public bool ShowGriffinButton { get; set; }
    public bool ShowGriffinUnavailableMessage { get; set; }

    /// <summary>
    /// When true, the email+password form, the divider, and the secondary
    /// Forgot-Password / Request-Access links are hidden from the page.
    /// Admins with local passwords reveal them by Ctrl+clicking the Shifty logo.
    /// Driven by the FF_HIDE_REGULAR_LOGIN feature flag (default OFF).
    /// </summary>
    public bool HideRegularLogin { get; set; }

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

        // FF_HIDE_REGULAR_LOGIN gates the email+password form on /Auth/Login. The view still
        // RENDERS the form (so the Ctrl+click escape hatch can show it) — it just initially
        // hides it via inline display:none. Server-side POST is intentionally NOT blocked:
        // the existing PasswordHash.Length==0 check below already prevents SSO-provisioned
        // users from authenticating with credentials, so the only people who can complete a
        // POST are admins with real local passwords (the intended escape-hatch population).
        HideRegularLogin = await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.HideRegularLogin);

        // ✅ FIX: Improved Griffin availability check with detailed logging
        var griffinConfig = await _griffinConfigService.GetGriffinConfigAsync();

        if (griffinConfig == null)
        {
            // Not configured at all (database table missing or appsettings disabled)
            ShowGriffinButton = false;
            ShowGriffinUnavailableMessage = false;
            LogGriffinConfigNotFound(_logger);
        }
        else if (!griffinConfig.Enabled)
        {
            // Configured but explicitly disabled
            ShowGriffinButton = false;
            ShowGriffinUnavailableMessage = false;
            LogGriffinDisabled(_logger, griffinConfig.CompanyId);
        }
        else if (string.IsNullOrWhiteSpace(griffinConfig.BaseUrl) ||
                 string.IsNullOrWhiteSpace(griffinConfig.TokenConsumerUrl))
        {
            // Enabled but incomplete configuration
            ShowGriffinButton = false;
            ShowGriffinUnavailableMessage = true;
            LogGriffinIncomplete(_logger,
                griffinConfig.BaseUrl ?? "(null)",
                griffinConfig.TokenConsumerUrl ?? "(null)");
        }
        else
        {
            // Fully configured — show the button based on configuration alone.
            // Connectivity is validated when the user clicks "Login with ADFS"
            // and can be diagnosed via /GriffinDiagnostic.
            // Previously, a live HTTP connection test ran here on every page load,
            // which silently hid the button on transient network/SSL/timeout failures.
            ShowGriffinButton = true;
            ShowGriffinUnavailableMessage = false;
            LogGriffinReady(_logger, griffinConfig.BaseUrl);
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

            // Audit the escape-hatch usage so an admin reviewing security logs can see
            // who bypassed FF_HIDE_REGULAR_LOGIN and from which IP. The escape hatch is
            // not a security boundary (PasswordHash.Length==0 still blocks ADFS users)
            // but the audit trail is.
            if (EscapeHatchUsed)
            {
                _logger.LogWarning(
                    "Login escape hatch used (Ctrl+click on logo) for {Email} from {IP}",
                    Services.PiiMasker.MaskEmail(Email ?? ""), ipAddress);
            }

            if (!_rateLimiting.IsAllowed(ipRateLimitKey, 10, 15))
            {
                LogIpRateLimitExceeded(_logger, ipAddress);
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
                    LogAccountRateLimitExceeded(_logger,
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
                LogOversizedInput(_logger, ipAddress);
                Error = _localizer["Error_InvalidInput"];
                return Page();
            }

            // ✅ SECURITY FIX: Proper email format validation with regex
            if (!_validation.IsValidEmail(Email))
            {
                Error = _localizer["Error_InvalidEmailFormat"];
                return Page();
            }

            // SECURITY-AUDITED: SAFE — login must search across all companies to authenticate users.
            // Case-insensitive email match (consistent with GriffinService, GriffinSignup,
            // ForgotPassword). A user signed up with "Foo@x.com" can log in with "foo@x.com" —
            // before this fix the lookup was case-sensitive and the case-mismatched user was a
            // ghost account that could not authenticate. .ToLower() translates to SQL LOWER();
            // .ToLowerInvariant() does NOT translate (root cause of GRIFFIN-USERLOOKUP-510).
            var emailLower = (Email ?? string.Empty).ToLowerInvariant();
            var user = await _db.Users
                .IgnoreQueryFilters() // Allow login across all companies
                .Include(u => u.RoleTemplate)
                .Include(u => u.JobType)
                .FirstOrDefaultAsync(u => u.Email.ToLower() == emailLower && u.IsActive);

            // ✅ SECURITY FIX: Account lockout protection
            if (user != null)
            {
                // Check if account is locked out
                if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTime.UtcNow)
                {
                    var remainingMinutes = (int)(user.LockoutEnd.Value - DateTime.UtcNow).TotalMinutes + 1;
                    LogAccountAlreadyLocked(_logger, ShiftManager.Services.PiiMasker.MaskEmail(Email), remainingMinutes);

                    // Set lockout properties for UI display
                    IsAccountLocked = true;
                    LockoutMinutesRemaining = remainingMinutes;
                    Error = _localizer["Error_Login_AccountLocked", remainingMinutes];
                    return Page();
                }

                // Record login attempt time
                user.LastLoginAttempt = DateTime.UtcNow;
            }

            // SSO users have no local password — redirect them to ADFS login
            if (user != null && user.PasswordHash.Length == 0)
            {
                LogSsoUserLocalAttempt(_logger, ShiftManager.Services.PiiMasker.MaskEmail(Email));
                Error = _localizer["Error_Login_SSOUserUseADFS"];
                return Page();
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

                        LogAccountLockedAfterAttempts(_logger, ShiftManager.Services.PiiMasker.MaskEmail(Email), user.FailedLoginAttempts);

                        // Set lockout properties for UI display
                        IsAccountLocked = true;
                        LockoutMinutesRemaining = 3;
                        Error = _localizer["Error_Login_AccountLockedAfterAttempts"];
                        return Page();
                    }

                    await _db.SaveChangesAsync();
                    LogLoginFailedAttempt(_logger, ShiftManager.Services.PiiMasker.MaskEmail(Email), user.FailedLoginAttempts);

                    // Set failed attempts count for warning display (only if user exists)
                    FailedAttemptsCount = user.FailedLoginAttempts;
                }
                else
                {
                    LogLoginFailedUserNotFound(_logger, ShiftManager.Services.PiiMasker.MaskEmail(Email));
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

            // Login-time backfill safety net: if user has no RoleTemplate, derive from Role + JobType
            if (user.RoleTemplateId == null)
            {
                var templateKey = MapUserRoleToRoleTemplateKey(user.Role, user.JobType?.Name);
                var template = await _roleService.GetRoleTemplateByKeyAsync(templateKey);
                if (template != null)
                {
                    user.RoleTemplateId = template.Id;
                    user.RoleTemplate = template;
                    if (template.DerivedUserRole.HasValue)
                        user.Role = template.DerivedUserRole.Value;
                    await _db.SaveChangesAsync();
                    LogRoleTemplateBackfill(_logger, template.Id, user.Id);
                }
            }

            // v3.0 Organizational Hierarchy - fetch early so we can use for grant provisioning and claims
            var hierarchyContext = await _hierarchyService.GetUserHierarchyContextAsync(user.Id);

            // Login-time grant reconciliation: ensure user's grants match their RoleTemplate's AutoGrants.
            // ApplyAutoGrantsAsync is idempotent — it only inserts grants that don't already exist,
            // so this safely picks up any new grants added to the template since the user was last provisioned.
            if (user.RoleTemplateId.HasValue)
            {
                var roleScope = new GrantScope(
                    ProjectId: hierarchyContext?.Path.Project?.Id,
                    AreaId: hierarchyContext?.Path.Area?.Id,
                    MoleculeId: hierarchyContext?.Path.Molecule?.Id,
                    DepartmentId: user.DepartmentId,
                    CompanyId: user.CompanyId,
                    JobTypeId: hierarchyContext?.JobType?.Id
                );
                await _grantService.ApplyAutoGrantsAsync(user.Id, user.RoleTemplateId.Value, roleScope);
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.DisplayName),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
                new Claim("CompanyId", user.CompanyId.ToString())
            };

            // Avatar claim for sidebar display (prevents 404 for users without avatars)
            if (!string.IsNullOrWhiteSpace(user.AvatarFileName))
            {
                claims.Add(new Claim("AvatarFileName", user.AvatarFileName));
            }

            // RoleTemplateKey claim for display and grant resolution
            if (user.RoleTemplate != null)
            {
                claims.Add(new Claim("RoleTemplateKey", user.RoleTemplate.Key));
            }
            if (hierarchyContext != null)
            {
                claims.Add(new Claim("MoleculeId", hierarchyContext.Path.Molecule.Id.ToString()));
                claims.Add(new Claim("AreaId", hierarchyContext.Path.Area.Id.ToString()));
                claims.Add(new Claim("ProjectId", hierarchyContext.Path.Project.Id.ToString()));
                claims.Add(new Claim("IsWorkforce", hierarchyContext.IsWorkforce.ToString()));
                claims.Add(new Claim("IsTech", hierarchyContext.IsTech.ToString()));

                if (hierarchyContext.JobType != null)
                {
                    claims.Add(new Claim("JobTypeId", hierarchyContext.JobType.Id.ToString()));
                    claims.Add(new Claim("JobTypeName", hierarchyContext.JobType.Name));
                }

                if (hierarchyContext.Path.Department != null)
                    claims.Add(new Claim("DepartmentId", hierarchyContext.Path.Department.Id.ToString()));
            }

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

            // Seed the theme cookie from DB so the next render derives --primary-* tokens
            // without a DB hit. Mirrors what /Api/My/Theme sets on save; readable by
            // theme-engine.js (HttpOnly=false) for pre-paint FOUC prevention.
            if (!string.IsNullOrEmpty(user.ThemeColor) || !string.IsNullOrEmpty(user.ThemeMode))
            {
                var themePayload = System.Text.Json.JsonSerializer.Serialize(new { color = user.ThemeColor, mode = user.ThemeMode });
                Response.Cookies.Append("theme", themePayload, new CookieOptions
                {
                    HttpOnly = false,
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax,
                    Secure = Request.IsHttps,
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    Path = "/"
                });
            }

            LogSignedInSuccessfully(_logger, user.Id, ShiftManager.Services.PiiMasker.MaskEmail(user.Email), user.Role);

            // A-07: Force redirect to password change page if MustChangePassword flag is set
            if (user.MustChangePassword)
            {
                LogMustChangePassword(_logger, user.Id);
                return RedirectToPage("/Auth/ForgotPassword");
            }

            // B-05: Redirect new users to onboarding wizard on first login
            if (!user.HasCompletedOnboarding)
            {
                LogOnboardingPending(_logger, user.Id);
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
            LogUnhandledLoginException(_logger, ex, ShiftManager.Services.PiiMasker.MaskEmail(Email));
            Error = _localizer["Error_UnexpectedError"];
            return Page();
        }
    }

    public async Task<IActionResult> OnPostGriffinAsync(string? returnUrl = null)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        LogGriffinAuthInitiated(_logger, ipAddress);

        var griffinConfig = await _griffinConfigService.GetGriffinConfigAsync();

        // Validate Griffin is enabled
        if (griffinConfig?.Enabled != true)
        {
            LogGriffinPostNotEnabled(_logger);
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
            LogGriffinPostBaseUrlMissing(_logger);
            Error = _localizer["Error_Login_AdfsNotConfigured"];
            ReturnUrl = returnUrl ?? "/";
            ShowGriffinButton = true;
            ShowGriffinUnavailableMessage = true;
            await OnGetAsync(returnUrl: returnUrl);
            return Page();
        }

        if (string.IsNullOrWhiteSpace(griffinConfig.TokenConsumerUrl))
        {
            LogGriffinPostTokenConsumerMissing(_logger);
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
            LogGriffinBaseUrlInvalidScheme(_logger, griffinConfig.BaseUrl);
            LogGriffinBaseUrlSchemeAdvice(_logger);
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
            LogGriffinTokenConsumerInvalidScheme(_logger, griffinConfig.TokenConsumerUrl);
            LogGriffinTokenConsumerSchemeAdvice(_logger);
            Error = "Griffin ADFS configuration error: Callback URL must start with http:// or https://. Please contact your administrator to fix this in /Owner/GriffinConfig.";
            ReturnUrl = returnUrl ?? "/";
            ShowGriffinButton = false;
            ShowGriffinUnavailableMessage = true;
            await OnGetAsync(returnUrl: returnUrl);
            return Page();
        }

        // ✅ FIX: Use configured TokenConsumerUrl from database (not Request.Scheme/Host)
        // IMPORTANT: Do NOT append returnUrl as a query parameter to the callback URL.
        // Adding ?returnUrl=... introduces a '?' that breaks the outer Griffin auth URL's
        // query string parsing — Griffin sees a truncated tokenConsumerURL and may not
        // redirect correctly (causing "token missing" errors).
        // Instead, store returnUrl in a temporary cookie and read it in the callback.
        var callbackUrl = griffinConfig.TokenConsumerUrl;

        if (!string.IsNullOrEmpty(returnUrl))
        {
            // Validate returnUrl is local BEFORE storing in the cookie. Url.IsLocalUrl correctly
            // rejects "//evil.com" (protocol-relative), "javascript:", "\evil.com", and absolute
            // off-site URLs. The same check runs at the callback's redirect site, but enforcing
            // here too means a poisoned returnUrl is never even stored.
            if (Url.IsLocalUrl(returnUrl))
            {
                Response.Cookies.Append("griffin.returnUrl", returnUrl, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = Request.IsHttps,
                    // SameSite=Lax is intentional and load-bearing: the Griffin redirect-back is
                    // a top-level cross-site GET and Strict would suppress the cookie, breaking
                    // returnUrl preservation. Lax is the correct mode for this exact pattern.
                    SameSite = SameSiteMode.Lax,
                    MaxAge = TimeSpan.FromMinutes(5),
                    // Belt-and-suspenders: set both MaxAge AND Expires. IE (still on some
                    // military workstations) does not honor Max-Age and would otherwise treat
                    // this as a session cookie. Expires uses the same 5-minute horizon.
                    Expires = DateTimeOffset.UtcNow.AddMinutes(5),
                    // Path="/" so the cookie is visible at /Auth/GriffinCallback regardless
                    // of IIS path-prefix configuration. The previous "/Auth" path scoping was
                    // fragile under virtual-directory deployments. The cookie name itself
                    // (griffin.returnUrl) is unique enough to avoid collisions site-wide.
                    Path = "/"
                });
                LogGriffinReturnUrlStored(_logger, returnUrl);
            }
            else
            {
                _logger.LogWarning(
                    "Refusing to store non-local returnUrl in griffin.returnUrl cookie: {ReturnUrl}",
                    returnUrl);
            }
        }

        LogGriffinDebugBanner(_logger);
        LogGriffinDebugConfigHeader(_logger);
        LogGriffinDebugBaseUrl(_logger, griffinConfig.BaseUrl);
        LogGriffinDebugTokenConsumerUrl(_logger, griffinConfig.TokenConsumerUrl);
        LogGriffinDebugCallbackUrl(_logger, callbackUrl);

        // Build authentication URL
        var authUrl = _griffinService.BuildAuthenticationUrl(griffinConfig.BaseUrl, callbackUrl);

        LogGriffinAuthUrlGenerated(_logger, authUrl);

        // ✅ CRITICAL FIX: Validate the generated URL is absolute before redirecting
        if (!Uri.TryCreate(authUrl, UriKind.Absolute, out var authUri))
        {
            LogGriffinAuthUrlNotAbsolute(_logger, authUrl);
            LogGriffinAuthUrlNotAbsoluteCause(_logger);
            LogGriffinAuthUrlNotAbsoluteAdvice(_logger);
            Error = "Griffin ADFS configuration error: Generated authentication URL is invalid. Please contact your administrator.";
            ReturnUrl = returnUrl ?? "/";
            ShowGriffinButton = false;
            ShowGriffinUnavailableMessage = true;
            await OnGetAsync(returnUrl: returnUrl);
            return Page();
        }

        LogGriffinUrlValidationHeader(_logger);
        LogGriffinUrlIsAbsolute(_logger);
        LogGriffinUrlScheme(_logger, authUri.Scheme);
        LogGriffinUrlHost(_logger, authUri.Host);
        LogGriffinUrlPath(_logger, authUri.AbsolutePath);
        LogGriffinUrlQuery(_logger, authUri.Query.Length > 100 ? authUri.Query.Substring(0, 100) + "..." : authUri.Query);
        LogGriffinRedirecting(_logger);
        LogGriffinDebugFooter(_logger);

        // Redirect to Griffin
        return Redirect(authUrl);
    }

    /// <summary>
    /// Maps legacy UserRole + JobType to RoleTemplate key for login-time backfill.
    /// Delegates to centralized RoleTemplateMapper to ensure consistency across codebase.
    /// </summary>
    private static string MapUserRoleToRoleTemplateKey(UserRole role, string? jobTypeName)
        => Helpers.RoleTemplateMapper.MapUserRoleToRoleTemplateKey(role, jobTypeName);
}
