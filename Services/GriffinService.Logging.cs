using Microsoft.Extensions.Logging;

namespace ShiftManager.Services;

// Source-generated LoggerMessage delegates for GriffinService.
// EventId range 17000-17099 reserved for GriffinService. Range allocations:
//   17000-17009: BuildAuthenticationUrl (4 used)
//   17010-17019: ExchangeTokenAsync (3 used)
//   17020-17029: ValidateTokenAsync (2 used)
//   17030-17039: GetClaimsAsync (5 used)
//   17040-17049: ValidateAndGetClaimsAsync cache (2 used)
//   17050-17059: AuthenticateUserAsync (3 used)
//   17060-17069: (vacated — formerly AutoProvisionUserAsync, removed when silent provisioning was retired)
//   17070-17079: CallGriffinGetAsync unhandled (1 used)
//   17080-17089: Pattern C reused — Griffin stage call failed (1 used)
public partial class GriffinService
{
    // ── BuildAuthenticationUrl ─────────────────────────────────────────────

    [LoggerMessage(EventId = 17000, Level = LogLevel.Debug,
        Message = "Building Griffin authentication URL:")]
    private static partial void LogBuildingAuthUrl(ILogger logger);

    [LoggerMessage(EventId = 17001, Level = LogLevel.Debug,
        Message = "  - Griffin BaseUrl: {BaseUrl}")]
    private static partial void LogAuthUrlBaseUrl(ILogger logger, string baseUrl);

    [LoggerMessage(EventId = 17002, Level = LogLevel.Debug,
        Message = "  - TokenConsumerUrl (NOT encoded): {TokenConsumerUrl}")]
    private static partial void LogAuthUrlTokenConsumerUrl(ILogger logger, string tokenConsumerUrl);

    [LoggerMessage(EventId = 17003, Level = LogLevel.Information,
        Message = "Griffin authentication URL constructed: {FinalUrl}")]
    private static partial void LogAuthUrlConstructed(ILogger logger, string finalUrl);

    // ── ExchangeTokenAsync ─────────────────────────────────────────────────

    [LoggerMessage(EventId = 17010, Level = LogLevel.Debug,
        Message = "Exchanging Griffin hashed token for JWT")]
    private static partial void LogExchangingToken(ILogger logger);

    [LoggerMessage(EventId = 17011, Level = LogLevel.Warning,
        Message = "Griffin token exchange returned unparsable body [{ErrorToken}]")]
    private static partial void LogTokenExchangeUnparsable(ILogger logger, string errorToken);

    [LoggerMessage(EventId = 17012, Level = LogLevel.Debug,
        Message = "Griffin token exchange successful, received JWT")]
    private static partial void LogTokenExchangeSuccess(ILogger logger);

    // ── ValidateTokenAsync ─────────────────────────────────────────────────

    [LoggerMessage(EventId = 17020, Level = LogLevel.Debug,
        Message = "Validating Griffin token (masked: ***)")]
    private static partial void LogValidatingToken(ILogger logger);

    [LoggerMessage(EventId = 17021, Level = LogLevel.Warning,
        Message = "Griffin validate returned unexpected shape [{ErrorToken}]")]
    private static partial void LogValidateUnexpectedShape(ILogger logger, string errorToken);

    // ── GetClaimsAsync ─────────────────────────────────────────────────────

    [LoggerMessage(EventId = 17030, Level = LogLevel.Debug,
        Message = "Fetching Griffin claims (token masked: ***)")]
    private static partial void LogFetchingClaims(ILogger logger);

    [LoggerMessage(EventId = 17031, Level = LogLevel.Warning,
        Message = "Griffin getClaims returned invalid JSON [{ErrorToken}]")]
    private static partial void LogGetClaimsInvalidJson(ILogger logger, string errorToken);

    [LoggerMessage(EventId = 17032, Level = LogLevel.Warning,
        Message = "Griffin getClaims missing EmailAddress [{ErrorToken}]")]
    private static partial void LogGetClaimsMissingEmail(ILogger logger, string errorToken);

    [LoggerMessage(EventId = 17033, Level = LogLevel.Warning,
        Message = "Griffin getClaims missing UniqueID [{ErrorToken}]")]
    private static partial void LogGetClaimsMissingUniqueId(ILogger logger, string errorToken);

    // Evidence-capture: logs the exact field names Griffin returned (presentKeys) plus the
    // values extracted for DisplayName / GivenName / Surname. Email is already logged here so
    // adding the other personal fields does not change net PII exposure — and these fields are
    // the diagnostic surface admins need when an ADFS claim mapping starts returning the wrong
    // attribute (e.g. unit name in the `name`/`cn` slot). Level = Information so it lands in
    // production logs without enabling Debug. EventId unchanged (17034) — same call site,
    // wider payload.
    [LoggerMessage(EventId = 17034, Level = LogLevel.Information,
        Message = "Griffin claims retrieved for {EmailAddress} | keys=[{PresentKeys}] DisplayName='{DisplayName}' GivenName='{GivenName}' Surname='{Surname}'")]
    private static partial void LogClaimsRetrieved(
        ILogger logger,
        string emailAddress,
        string presentKeys,
        string displayName,
        string givenName,
        string surname);

    // ── ValidateAndGetClaimsAsync cache ────────────────────────────────────

    [LoggerMessage(EventId = 17040, Level = LogLevel.Debug,
        Message = "Griffin claims cache hit")]
    private static partial void LogClaimsCacheHit(ILogger logger);

    [LoggerMessage(EventId = 17041, Level = LogLevel.Debug,
        Message = "Griffin claims cache miss, calling API")]
    private static partial void LogClaimsCacheMiss(ILogger logger);

    // ── AuthenticateUserAsync ──────────────────────────────────────────────

    [LoggerMessage(EventId = 17050, Level = LogLevel.Information,
        Message = "Griffin-authenticated user {Email} has no ShiftManager account; callback will route based on FF_ALLOW_USERS_CREATION_VIA_ADFS")]
    private static partial void LogUserNotRegistered(ILogger logger, string email);

    [LoggerMessage(EventId = 17051, Level = LogLevel.Information,
        Message = "Backfilled RoleTemplateId={TemplateId} for Griffin user {UserId}")]
    private static partial void LogBackfilledRoleTemplate(ILogger logger, int templateId, int userId);

    [LoggerMessage(EventId = 17052, Level = LogLevel.Information,
        Message = "Griffin authentication successful for user {UserId} ({Email})")]
    private static partial void LogAuthenticationSuccess(ILogger logger, int userId, string email);

    // ── CallGriffinGetAsync unhandled ──────────────────────────────────────

    [LoggerMessage(EventId = 17070, Level = LogLevel.Error,
        Message = "Unhandled exception calling Griffin endpoint {Url}")]
    private static partial void LogUnhandledHttpException(ILogger logger, System.Exception ex, string url);

    // ── Pattern C reused — Griffin stage call failed ───────────────────────
    // Used by token exchange (TokenExchange), token validation (TokenValidation),
    // and getClaims (ClaimsRetrieval) — three sites share this template, differing
    // only by the stage descriptor in the message text.

    [LoggerMessage(EventId = 17080, Level = LogLevel.Warning,
        Message = "Griffin {Stage} call failed [{ErrorToken}]: {Detail}")]
    private static partial void LogStageCallFailed(ILogger logger, string stage, string errorToken, string detail);
}
