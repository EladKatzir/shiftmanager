using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models.Support;
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

    // Structured failure display — populated when any GriffinApiResult fails.
    // The view renders these directly instead of the single generic Error string.
    public string? FailureTitle { get; set; }
    public string? FailureDetail { get; set; }
    public string? FailureRemediation { get; set; }
    public string? FailureErrorToken { get; set; }
    public string? FailureStage { get; set; }

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
        // 1. Extract token — Griffin may send as "token", "HashedToken", or "hasedToken" (typo tolerated).
        var effectiveToken = token ?? hashedToken ?? hasedToken;
        if (string.IsNullOrEmpty(effectiveToken))
        {
            _logger.LogWarning("Griffin callback invoked without token parameter. Query: {Query}",
                Request.QueryString.Value);
            return ShowFailure(new GriffinApiError(
                GriffinStage.TokenExchange,
                GriffinErrorCode.EmptyResponse,
                "Griffin callback was invoked without any of the expected token query parameters (token, HashedToken, hasedToken)."));
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

        // 3. Exchange hashed token for JWT.
        var exchangeResult = await _griffinService.ExchangeTokenAsync(
            token, griffinConfig.BaseUrl!, griffinConfig.TimeoutSeconds);
        if (!exchangeResult.Success)
            return ShowFailure(exchangeResult.Error!);

        token = exchangeResult.Value!;   // All subsequent operations use the JWT.

        // 4. Authenticate user.
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        GriffinApiResult<ClaimsPrincipal> authResult;
        try
        {
            authResult = await _griffinService.AuthenticateUserAsync(token, griffinConfig, ipAddress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Griffin AuthenticateUserAsync threw");
            _securityLogger.LogAuthenticationFailure("Griffin ADFS", ipAddress, ex.Message);
            return ShowFailure(new GriffinApiError(
                GriffinStage.UserLookup,
                GriffinErrorCode.UnhandledException,
                $"AuthenticateUserAsync threw {ex.GetType().Name}: {ex.Message}"));
        }

        if (!authResult.Success)
        {
            var err = authResult.Error!;

            // Special case: valid Griffin user with no ShiftManager account AND auto-provisioning disabled
            // → redirect to the signup page (existing product behaviour). The JWT is cached, so
            // ValidateAndGetClaimsAsync below is a cheap cache hit.
            if (err.Code == GriffinErrorCode.UserNotRegistered)
            {
                var claimsResult = await _griffinService.ValidateAndGetClaimsAsync(
                    token, griffinConfig.BaseUrl!, griffinConfig.TimeoutSeconds);
                if (claimsResult.Success && claimsResult.Value != null)
                {
                    Response.Cookies.Append("griffin.token", token, new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = Request.IsHttps,
                        SameSite = SameSiteMode.Lax,
                        Expires = DateTimeOffset.UtcNow.AddMinutes(15),
                        Path = "/"
                    });

                    TempData["GriffinEmail"] = claimsResult.Value.EmailAddress;
                    TempData["GriffinDisplayName"] = claimsResult.Value.DisplayName;
                    TempData["GriffinUniqueID"] = claimsResult.Value.UniqueID;

                    _logger.LogInformation("Griffin user {Email} not found in ShiftManager, redirecting to signup",
                        claimsResult.Value.EmailAddress);
                    return RedirectToPage("/Auth/GriffinSignup");
                }
                // Fall through and show the UserNotRegistered failure.
            }

            return ShowFailure(err);
        }

        var principal = authResult.Value!;

        // 5. Store the JWT as a cookie so GriffinAuthenticationMiddleware can re-validate on subsequent requests.
        Response.Cookies.Append("griffin.token", token, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddHours(8),
            Path = "/"
        });

        // 6. Sign in with ASP.NET cookie-auth for framework compatibility.
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            });

        // 7. Log success.
        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = principal.FindFirst(ClaimTypes.Email)?.Value;
        var role = principal.FindFirst(ClaimTypes.Role)?.Value;
        if (int.TryParse(userId, out var parsedUid))
            _securityLogger.LogAuthenticationSuccess(parsedUid, email ?? "", role ?? "", ipAddress);

        // 8. Resolve returnUrl — query first, cookie second.
        if (string.IsNullOrEmpty(returnUrl))
        {
            returnUrl = Request.Cookies["griffin.returnUrl"];
            if (!string.IsNullOrEmpty(returnUrl))
                _logger.LogDebug("Read returnUrl from cookie: {ReturnUrl}", returnUrl);
        }
        Response.Cookies.Delete("griffin.returnUrl", new CookieOptions { Path = "/Auth" });

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToPage("/Home/Index");
    }

    private IActionResult ShowFailure(GriffinApiError error)
    {
        var msg = GriffinErrorMessages.Describe(error, _localizer);
        FailureTitle = msg.Title;
        FailureDetail = msg.Detail;
        FailureRemediation = msg.Remediation;
        FailureErrorToken = msg.ErrorToken;
        FailureStage = error.Stage.ToString();
        Error = msg.Title; // keep the legacy single-string Error populated for anything that reads it
        return Page();
    }
}
