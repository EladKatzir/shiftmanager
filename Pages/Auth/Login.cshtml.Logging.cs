using Microsoft.Extensions.Logging;
using ShiftManager.Models.Support;

namespace ShiftManager.Pages.Auth;

// Source-generated LoggerMessage delegates for LoginModel (closes 43 sites of F-C-013).
// EventId range 1000-1099 reserved for /Auth/Login. Range allocations:
//   1000-1009: OnGetAsync — Griffin probe (4 used: 1000-1003)
//   1010-1029: OnPostAsync — auth core (13 used: 1010-1022)
//   1030-1099: OnPostGriffinAsync — ADFS redirect (26 used: 1030-1055)
public partial class LoginModel
{
    // ── OnGetAsync ─────────────────────────────────────────────────────────

    [LoggerMessage(EventId = 1000, Level = LogLevel.Debug,
        Message = "Griffin config not found (database table may be missing or appsettings.json has Enabled=false)")]
    private static partial void LogGriffinConfigNotFound(ILogger logger);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Debug,
        Message = "Griffin config exists but Enabled=false for company {CompanyId}")]
    private static partial void LogGriffinDisabled(ILogger logger, int companyId);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning,
        Message = "Griffin enabled but configuration incomplete: BaseUrl={BaseUrl}, TokenConsumerUrl={TokenConsumerUrl}")]
    private static partial void LogGriffinIncomplete(ILogger logger, string baseUrl, string tokenConsumerUrl);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Debug,
        Message = "Griffin ADFS is configured and enabled for BaseUrl={BaseUrl}, showing login option")]
    private static partial void LogGriffinReady(ILogger logger, string baseUrl);

    // ── OnPostAsync ────────────────────────────────────────────────────────

    [LoggerMessage(EventId = 1010, Level = LogLevel.Warning,
        Message = "Rate limit exceeded for login from IP: {IP}")]
    private static partial void LogIpRateLimitExceeded(ILogger logger, string ip);

    [LoggerMessage(EventId = 1011, Level = LogLevel.Warning,
        Message = "Per-account rate limit exceeded for {Email} from IP {IP} (possible multi-IP attack)")]
    private static partial void LogAccountRateLimitExceeded(ILogger logger, string email, string ip);

    [LoggerMessage(EventId = 1012, Level = LogLevel.Warning,
        Message = "Login attempt with oversized input from IP {IP}")]
    private static partial void LogOversizedInput(ILogger logger, string ip);

    [LoggerMessage(EventId = 1013, Level = LogLevel.Warning,
        Message = "Login attempt for locked account: {Email}. Lockout ends in {Minutes} minutes")]
    private static partial void LogAccountAlreadyLocked(ILogger logger, string email, int minutes);

    [LoggerMessage(EventId = 1014, Level = LogLevel.Information,
        Message = "SSO user {Email} attempted local login, redirecting to ADFS")]
    private static partial void LogSsoUserLocalAttempt(ILogger logger, string email);

    [LoggerMessage(EventId = 1015, Level = LogLevel.Warning,
        Message = "Account locked for {Email} after {Attempts} failed attempts")]
    private static partial void LogAccountLockedAfterAttempts(ILogger logger, string email, int attempts);

    [LoggerMessage(EventId = 1016, Level = LogLevel.Warning,
        Message = "Login failed for {Email} (attempt {Attempt}/10)")]
    private static partial void LogLoginFailedAttempt(ILogger logger, string email, int attempt);

    [LoggerMessage(EventId = 1017, Level = LogLevel.Warning,
        Message = "Login failed for {Email} (user not found)")]
    private static partial void LogLoginFailedUserNotFound(ILogger logger, string email);

    [LoggerMessage(EventId = 1018, Level = LogLevel.Information,
        Message = "Backfilled RoleTemplateId={TemplateId} for user {UserId} at login")]
    private static partial void LogRoleTemplateBackfill(ILogger logger, int templateId, int userId);

    [LoggerMessage(EventId = 1019, Level = LogLevel.Information,
        Message = "User {UserId} ({Email}) signed in successfully. Role={Role}")]
    private static partial void LogSignedInSuccessfully(ILogger logger, int userId, string email, UserRole role);

    [LoggerMessage(EventId = 1020, Level = LogLevel.Information,
        Message = "User {UserId} must change password — redirecting to ForgotPassword")]
    private static partial void LogMustChangePassword(ILogger logger, int userId);

    [LoggerMessage(EventId = 1021, Level = LogLevel.Information,
        Message = "User {UserId} has not completed onboarding — redirecting to wizard")]
    private static partial void LogOnboardingPending(ILogger logger, int userId);

    [LoggerMessage(EventId = 1022, Level = LogLevel.Error,
        Message = "Unhandled exception during login for {Email}")]
    private static partial void LogUnhandledLoginException(ILogger logger, System.Exception ex, string email);

    // ── OnPostGriffinAsync ─────────────────────────────────────────────────

    [LoggerMessage(EventId = 1030, Level = LogLevel.Information,
        Message = "Griffin authentication initiated from IP {IP}")]
    private static partial void LogGriffinAuthInitiated(ILogger logger, string ip);

    [LoggerMessage(EventId = 1031, Level = LogLevel.Warning,
        Message = "Griffin authentication attempt but config not enabled (config null or Enabled=false)")]
    private static partial void LogGriffinPostNotEnabled(ILogger logger);

    [LoggerMessage(EventId = 1032, Level = LogLevel.Error,
        Message = "Griffin enabled but BaseUrl is missing")]
    private static partial void LogGriffinPostBaseUrlMissing(ILogger logger);

    [LoggerMessage(EventId = 1033, Level = LogLevel.Error,
        Message = "Griffin enabled but TokenConsumerUrl is missing")]
    private static partial void LogGriffinPostTokenConsumerMissing(ILogger logger);

    [LoggerMessage(EventId = 1034, Level = LogLevel.Error,
        Message = "CRITICAL: Griffin BaseUrl is missing scheme or invalid: '{BaseUrl}'")]
    private static partial void LogGriffinBaseUrlInvalidScheme(ILogger logger, string baseUrl);

    [LoggerMessage(EventId = 1035, Level = LogLevel.Error,
        Message = "BaseUrl must start with http:// or https://. Current value will cause 404 redirect error.")]
    private static partial void LogGriffinBaseUrlSchemeAdvice(ILogger logger);

    [LoggerMessage(EventId = 1036, Level = LogLevel.Error,
        Message = "CRITICAL: Griffin TokenConsumerUrl is missing scheme or invalid: '{TokenConsumerUrl}'")]
    private static partial void LogGriffinTokenConsumerInvalidScheme(ILogger logger, string tokenConsumerUrl);

    [LoggerMessage(EventId = 1037, Level = LogLevel.Error,
        Message = "TokenConsumerUrl must start with http:// or https://. Current value will cause authentication failure.")]
    private static partial void LogGriffinTokenConsumerSchemeAdvice(ILogger logger);

    [LoggerMessage(EventId = 1038, Level = LogLevel.Debug,
        Message = "Stored returnUrl in cookie: {ReturnUrl}")]
    private static partial void LogGriffinReturnUrlStored(ILogger logger, string returnUrl);

    [LoggerMessage(EventId = 1039, Level = LogLevel.Information,
        Message = "=== GRIFFIN ADFS REDIRECT DEBUG ===")]
    private static partial void LogGriffinDebugBanner(ILogger logger);

    [LoggerMessage(EventId = 1040, Level = LogLevel.Information,
        Message = "Config from database:")]
    private static partial void LogGriffinDebugConfigHeader(ILogger logger);

    [LoggerMessage(EventId = 1041, Level = LogLevel.Information,
        Message = "  - BaseUrl: {BaseUrl}")]
    private static partial void LogGriffinDebugBaseUrl(ILogger logger, string baseUrl);

    [LoggerMessage(EventId = 1042, Level = LogLevel.Information,
        Message = "  - TokenConsumerUrl: {TokenConsumerUrl}")]
    private static partial void LogGriffinDebugTokenConsumerUrl(ILogger logger, string tokenConsumerUrl);

    [LoggerMessage(EventId = 1043, Level = LogLevel.Information,
        Message = "  - CallbackUrl (clean, no query params): {CallbackUrl}")]
    private static partial void LogGriffinDebugCallbackUrl(ILogger logger, string callbackUrl);

    [LoggerMessage(EventId = 1044, Level = LogLevel.Information,
        Message = "Generated authentication URL: {AuthUrl}")]
    private static partial void LogGriffinAuthUrlGenerated(ILogger logger, string authUrl);

    [LoggerMessage(EventId = 1045, Level = LogLevel.Error,
        Message = "CRITICAL: Generated auth URL is NOT absolute: '{AuthUrl}'")]
    private static partial void LogGriffinAuthUrlNotAbsolute(ILogger logger, string authUrl);

    [LoggerMessage(EventId = 1046, Level = LogLevel.Error,
        Message = "This will cause ASP.NET to treat it as a relative path, resulting in 404 error.")]
    private static partial void LogGriffinAuthUrlNotAbsoluteCause(ILogger logger);

    [LoggerMessage(EventId = 1047, Level = LogLevel.Error,
        Message = "Check that BaseUrl starts with http:// or https://")]
    private static partial void LogGriffinAuthUrlNotAbsoluteAdvice(ILogger logger);

    [LoggerMessage(EventId = 1048, Level = LogLevel.Information,
        Message = "URL validation:")]
    private static partial void LogGriffinUrlValidationHeader(ILogger logger);

    [LoggerMessage(EventId = 1049, Level = LogLevel.Information,
        Message = "  - Is Absolute: YES ✓")]
    private static partial void LogGriffinUrlIsAbsolute(ILogger logger);

    [LoggerMessage(EventId = 1050, Level = LogLevel.Information,
        Message = "  - Scheme: {Scheme}")]
    private static partial void LogGriffinUrlScheme(ILogger logger, string scheme);

    [LoggerMessage(EventId = 1051, Level = LogLevel.Information,
        Message = "  - Host: {Host}")]
    private static partial void LogGriffinUrlHost(ILogger logger, string host);

    [LoggerMessage(EventId = 1052, Level = LogLevel.Information,
        Message = "  - Path: {Path}")]
    private static partial void LogGriffinUrlPath(ILogger logger, string path);

    [LoggerMessage(EventId = 1053, Level = LogLevel.Information,
        Message = "  - Query: {Query}")]
    private static partial void LogGriffinUrlQuery(ILogger logger, string query);

    [LoggerMessage(EventId = 1054, Level = LogLevel.Information,
        Message = "Redirecting browser to Griffin ADFS...")]
    private static partial void LogGriffinRedirecting(ILogger logger);

    [LoggerMessage(EventId = 1055, Level = LogLevel.Information,
        Message = "=== END DEBUG ===")]
    private static partial void LogGriffinDebugFooter(ILogger logger);
}
