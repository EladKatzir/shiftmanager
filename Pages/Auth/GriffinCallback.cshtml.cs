using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Data.SeedData;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Pages.Auth;

[AllowAnonymous]
public class GriffinCallbackModel : LocalizedPageModel
{
    private readonly IGriffinService _griffinService;
    private readonly IGriffinConfigService _griffinConfigService;
    private readonly IFeatureFlagService _featureFlagService;
    private readonly ISecurityLogger _securityLogger;
    private readonly ILogger<GriffinCallbackModel> _logger;
    private readonly IGriffinAuthDiagnostics _diagnostics;
    // SECURITY-AUDITED: SAFE — IgnoreQueryFilters used only for cross-company deactivated-user check
    // before tenant context is established; scoped by explicit email comparison
    private readonly AppDbContext _db;

    // Captures the time spent inside OnGetAsync — recorded in the diagnostics buffer so
    // an admin can see how long failed/successful login attempts actually took. Useful
    // for spotting "Griffin is alive but slow" vs "Griffin is unreachable" patterns.
    private readonly Stopwatch _sw = new();

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
        IFeatureFlagService featureFlagService,
        ISecurityLogger securityLogger,
        ILogger<GriffinCallbackModel> logger,
        AppDbContext db,
        IGriffinAuthDiagnostics diagnostics)
        : base(localizer)
    {
        _griffinService = griffinService;
        _griffinConfigService = griffinConfigService;
        _featureFlagService = featureFlagService;
        _securityLogger = securityLogger;
        _logger = logger;
        _db = db;
        _diagnostics = diagnostics;
    }

    // Server-side token shape gate. Griffin tokens are SHA-256 hex hashes (~64 chars) on
    // the inbound /Auth/GriffinCallback?token=... or compact JWS strings (3 dot-segments,
    // base64url) on the outbound chain. Either way they are bounded in length and use a
    // restricted alphabet. Reject anything outside that envelope BEFORE we hand the value
    // to GriffinService — defensive guard against URL crafting / smuggling. 4000 chars
    // is comfortably above any plausible JWT length (typical Griffin JWT ~600B).
    private const int MaxInboundTokenLength = 4000;
    private static bool IsAcceptableInboundToken(string token)
    {
        if (token.Length is 0 or > MaxInboundTokenLength) return false;
        foreach (var c in token)
        {
            // base64url alphabet + JWT segment separator + URL-safe padding
            if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' || c == '='))
                return false;
        }
        return true;
    }

    public async Task<IActionResult> OnGetAsync(
        [FromQuery(Name = "token")] string? token,
        [FromQuery(Name = "HashedToken")] string? hashedToken,
        [FromQuery(Name = "hasedToken")] string? hasedToken,
        string? returnUrl = null)
    {
        _sw.Restart();
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
        if (!IsAcceptableInboundToken(effectiveToken))
        {
            _logger.LogWarning("Griffin callback rejected — token failed shape validation (len={Len})",
                effectiveToken.Length);
            _securityLogger.LogSecurityThreat("InvalidTokenShape",
                $"Inbound Griffin token failed shape validation (length={effectiveToken.Length}). Possible URL crafting attempt.",
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            return ShowFailure(new GriffinApiError(
                GriffinStage.TokenExchange,
                GriffinErrorCode.UnexpectedResponseShape,
                "Inbound token failed shape validation (length or characters outside the base64url envelope)."));
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
        // Exclude OperationCanceledException — that's the request being aborted, not a Griffin
        // failure. Letting it propagate avoids polluting the diagnostics buffer with noise.
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Griffin AuthenticateUserAsync threw");
            _securityLogger.LogAuthenticationFailure("Griffin ADFS", ipAddress, ex.Message);
            // Record the FULL exception (type, message, stack, inner chain) in the diagnostics
            // ring buffer so /GriffinDiagnostic can render it. This is the bridge that lets an
            // air-gapped admin see the actual exception without server-file access.
            var err = new GriffinApiError(
                GriffinStage.UserLookup,
                GriffinErrorCode.UnhandledException,
                $"AuthenticateUserAsync threw {ex.GetType().Name}: {ex.Message}");
            // Try to retrieve the email from the claims cache so the failure event isn't
            // anonymous. The token was already validated above, so this almost-always hits
            // the positive cache and is essentially free.
            var emailForEvent = await TryGetClaimsEmailAsync(token, griffinConfig);
            RecordFailure(err, ex, email: emailForEvent, ipAddress);
            return ShowFailure(err);
        }

        if (!authResult.Success)
        {
            var err = authResult.Error!;
            // Record EVERY service-side failure (covers the new stage-specific codes 510-550
            // we just added in AuthenticateUserAsync). Even the "UserNotRegistered" path gets
            // recorded so an admin can see which ADFS identities tried to sign in.
            // Pull the email from claims cache so the failure event identifies the user.
            var emailForEvent = await TryGetClaimsEmailAsync(token, griffinConfig);
            RecordFailure(err, exception: null, email: emailForEvent, ipAddress);

            // Valid Griffin user with no ShiftManager account. The FF_ALLOW_USERS_CREATION_VIA_ADFS
            // feature flag (managed at /Owner/FeatureFlags) decides what happens next:
            //   - ON  → redirect to GriffinSignup so the user can file a join request (admin-approved).
            //   - OFF → render a refusal page asking the user to contact the officer near their home.
            // We never silently create accounts — that path was retired with the security review.
            if (err.Code == GriffinErrorCode.UserNotRegistered)
            {
                var allowSignup = await _featureFlagService.IsEnabledAsync(
                    FeatureFlagSeed.Flags.AllowUsersCreationViaAdfs);

                if (allowSignup)
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
                        TempData["GriffinGivenName"] = claimsResult.Value.GivenName;
                        TempData["GriffinSurname"] = claimsResult.Value.Surname;
                        TempData["GriffinUniqueID"] = claimsResult.Value.UniqueID;

                        _logger.LogInformation("Griffin user {Email} not found in ShiftManager, redirecting to signup",
                            claimsResult.Value.EmailAddress);
                        return RedirectToPage("/Auth/GriffinSignup");
                    }
                    // Claims fetch failed mid-way — fall through to the generic UserNotRegistered failure.
                }
                else
                {
                    // Refusal path: do NOT set the griffin.token cookie — there's no signup form coming,
                    // so caching the JWT serves no purpose and lengthens the window the token is on disk.
                    //
                    // Audit the refusal: a holder of a valid Griffin credential who is NOT in
                    // ShiftManager is operationally relevant (someone with military ADFS auth
                    // probing for access). Without this entry the event is invisible in logs.
                    _logger.LogInformation(
                        "Griffin user not found and FF_ALLOW_USERS_CREATION_VIA_ADFS is disabled; showing refusal page");
                    _securityLogger.LogAuthenticationFailure(
                        "Griffin ADFS (no-account refused)",
                        ipAddress,
                        $"ADFS-authenticated identity has no ShiftManager account; user-creation FF is OFF. ErrorToken={err.ErrorToken}");
                    FailureTitle = _localizer["Error_GriffinNoAccess_Title"].Value;
                    FailureDetail = _localizer["Error_GriffinNoAccess_Detail"].Value;
                    FailureRemediation = _localizer["Error_GriffinNoAccess_Remediation"].Value;
                    FailureErrorToken = err.ErrorToken;
                    FailureStage = err.Stage.ToString();
                    Error = FailureTitle;
                    return Page();
                }
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
        int? parsedUidNullable = null;
        if (int.TryParse(userId, out var parsedUid))
        {
            _securityLogger.LogAuthenticationSuccess(parsedUid, email ?? "", role ?? "", ipAddress);
            parsedUidNullable = parsedUid;
        }
        _sw.Stop();
        _diagnostics.Record(new GriffinAuthEvent(
            TimestampUtc: DateTime.UtcNow,
            Success: true,
            FailureStage: null,
            FailureCode: null,
            ErrorToken: null,
            Email: email,
            IpAddress: ipAddress,
            UserId: parsedUidNullable,
            ExceptionType: null,
            ExceptionMessage: null,
            StackTrace: null,
            TechnicalDetail: null,
            DurationMs: (int)_sw.ElapsedMilliseconds));

        // 8. Resolve returnUrl — query first, cookie second.
        if (string.IsNullOrEmpty(returnUrl))
        {
            returnUrl = Request.Cookies["griffin.returnUrl"];
            if (!string.IsNullOrEmpty(returnUrl))
                _logger.LogDebug("Read returnUrl from cookie: {ReturnUrl}", returnUrl);
        }
        // Path="/" matches the cookie's scoping at Login.cshtml.cs (set with the same root
        // path so it's visible across the whole site, including /Auth/GriffinCallback under
        // IIS path-prefix deployments).
        Response.Cookies.Delete("griffin.returnUrl", new CookieOptions { Path = "/" });

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

    /// <summary>
    /// Capture a failure into the in-memory diagnostics buffer. Pulls the full exception
    /// chain (type, message, stack trace) so the admin diagnostic page can render it.
    /// </summary>
    private void RecordFailure(GriffinApiError err, Exception? exception, string? email, string ipAddress)
    {
        _sw.Stop();
        var (exType, exMessage, stackTrace) = ExtractExceptionDetail(exception);
        _diagnostics.Record(new GriffinAuthEvent(
            TimestampUtc: DateTime.UtcNow,
            Success: false,
            FailureStage: err.Stage,
            FailureCode: err.Code,
            ErrorToken: err.ErrorToken,
            Email: email,
            IpAddress: ipAddress,
            UserId: null,
            ExceptionType: exType,
            ExceptionMessage: exMessage,
            StackTrace: stackTrace,
            TechnicalDetail: err.TechnicalDetail,
            DurationMs: (int)_sw.ElapsedMilliseconds));
        _sw.Restart(); // in case the request keeps running
    }

    /// <summary>
    /// Best-effort retrieval of the validated ADFS email for diagnostic-event labeling. Hits the
    /// positive claims cache populated by ValidateAndGetClaimsAsync; returns null if the cache
    /// missed or the token is no longer valid. Wrapped in a permissive try/catch so a problem
    /// here can never break the failure-reporting path.
    /// </summary>
    private async Task<string?> TryGetClaimsEmailAsync(string token, ShiftManager.Models.GriffinConfig config)
    {
        try
        {
            if (string.IsNullOrEmpty(config.BaseUrl)) return null;
            var claimsResult = await _griffinService.ValidateAndGetClaimsAsync(
                token, config.BaseUrl, config.TimeoutSeconds);
            return claimsResult.Success ? claimsResult.Value?.EmailAddress : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Walk the inner-exception chain so wrapped exceptions (DbUpdateException → SqliteException → ...)
    /// surface all the way down. Returns a single composite message and stack.
    /// </summary>
    private static (string? Type, string? Message, string? Stack) ExtractExceptionDetail(Exception? ex)
    {
        if (ex == null) return (null, null, null);
        var types = new List<string>();
        var messages = new List<string>();
        for (var cur = ex; cur != null; cur = cur.InnerException)
        {
            types.Add(cur.GetType().FullName ?? cur.GetType().Name);
            messages.Add(cur.Message);
        }
        return (
            string.Join(" → ", types),
            string.Join("  ||  ", messages),
            ex.ToString()  // full stack including inner chain
        );
    }
}
