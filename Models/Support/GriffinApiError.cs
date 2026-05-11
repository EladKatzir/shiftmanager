namespace ShiftManager.Models.Support;

/// <summary>
/// Which stage of the Griffin ADFS flow failed. Part of the stable error token
/// we show to the user so an admin can quickly identify which HTTP call broke.
/// </summary>
public enum GriffinStage
{
    TokenExchange,        // POST/GET /authentication/claimToken
    TokenValidation,      // GET /authorization/validate
    ClaimsRetrieval,      // GET /authorization/getClaims
    UserLookup,           // Local DB lookup after claims retrieved
    UserProvisioning,     // Auto-provision branch
}

/// <summary>
/// Classification of every way a Griffin call can fail. Codes are stable; do
/// not renumber. Gaps are intentional — groups follow 1xx/2xx/... for quick
/// triage (1xx = network, 2xx = HTTP status, 3xx = body parsing, 4xx = claims,
/// 5xx = user mapping, 9xx = unhandled).
/// </summary>
public enum GriffinErrorCode
{
    None = 0,

    NetworkDnsFailure = 100,
    NetworkConnectionRefused = 101,
    NetworkTimeout = 102,
    NetworkSslError = 103,
    NetworkOther = 104,

    HttpBadRequest = 200,     // 400
    HttpUnauthorized = 201,   // 401
    HttpForbidden = 202,      // 403
    HttpNotFound = 203,       // 404
    HttpServerError = 204,    // 5xx
    HttpOther = 205,

    EmptyResponse = 300,
    UnreadableResponse = 301,        // Not valid JSON where JSON was required
    UnexpectedResponseShape = 302,   // Valid shape but not the one we expected

    MissingEmailAddress = 400,
    MissingUniqueId = 401,

    UserNotRegistered = 500,
    UserDeactivated = 501,
    AutoProvisionFailed = 502,

    // Stage-specific instrumentation (added 2026-05-11 to replace bare UnhandledException
    // bubble-ups during user lookup). Each value pinpoints WHICH sub-step of
    // AuthenticateUserAsync threw, so the error token tells the admin where to look
    // without needing to read server logs.
    UserLookupQueryFailed = 510,        // EF query on Users threw (DB connection, query translation, etc.)
    RoleTemplateBackfillFailed = 520,   // Backfill SaveChangesAsync threw
    HierarchyLoadFailed = 530,          // IHierarchyService.GetUserHierarchyContextAsync threw
    GrantApplicationFailed = 540,       // IGrantService.ApplyAutoGrantsAsync threw
    ClaimsPrincipalBuildFailed = 550,   // Sanitization or Claim construction threw

    UnhandledException = 900,
}

/// <summary>
/// Structured description of a Griffin failure. TechnicalDetail is for logs
/// only — never show it raw to end users because it can contain response
/// bodies that may leak tokens or server identifiers.
/// </summary>
public record GriffinApiError(
    GriffinStage Stage,
    GriffinErrorCode Code,
    string TechnicalDetail,
    string? Host = null,
    int? HttpStatus = null,
    string? ResponsePreview = null)
{
    /// <summary>
    /// Stable identifier the user can quote to an admin. Format is
    /// "GRIFFIN-{STAGE}-{CODE}" where STAGE is uppercase enum name and CODE is
    /// the numeric <see cref="GriffinErrorCode"/> value.
    /// </summary>
    public string ErrorToken => $"GRIFFIN-{Stage.ToString().ToUpperInvariant()}-{(int)Code}";
}

/// <summary>
/// Result wrapper. Exactly one of Value/Error is populated. Callers check
/// <see cref="Success"/> first, then read Value or Error accordingly.
/// </summary>
public record GriffinApiResult<T>(T? Value, GriffinApiError? Error)
{
    public bool Success => Error == null;

    public static GriffinApiResult<T> Ok(T value) => new(value, null);
    public static GriffinApiResult<T> Fail(GriffinApiError error) => new(default, error);

    /// <summary>
    /// Rewraps an error from a different T into this T. Useful for propagating
    /// errors up a chain of typed calls.
    /// </summary>
    public static GriffinApiResult<T> FailFrom<TOther>(GriffinApiResult<TOther> other)
    {
        if (other.Error == null)
            throw new InvalidOperationException("FailFrom called on a successful result");
        return new GriffinApiResult<T>(default, other.Error);
    }
}
