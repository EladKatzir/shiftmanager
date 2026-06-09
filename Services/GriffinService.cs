using System.Globalization;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.DTOs;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — SSO auth flow requires cross-company user search
// before tenant context is established; scoped by explicit email/companyId parameters
public partial class GriffinService : IGriffinService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AppDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly ISecurityLogger _securityLogger;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<GriffinService> _logger;
    private readonly IHierarchyService _hierarchyService;
    private readonly IGrantService _grantService;
    private readonly ICompanyMembershipService _companyMembershipService;

    public GriffinService(
        IHttpClientFactory httpClientFactory,
        AppDbContext dbContext,
        IMemoryCache cache,
        ISecurityLogger securityLogger,
        IAuditLogService auditLogService,
        ILogger<GriffinService> logger,
        IHierarchyService hierarchyService,
        IGrantService grantService,
        ICompanyMembershipService companyMembershipService)
    {
        _httpClientFactory = httpClientFactory;
        _dbContext = dbContext;
        _cache = cache;
        _securityLogger = securityLogger;
        _auditLogService = auditLogService;
        _logger = logger;
        _hierarchyService = hierarchyService;
        _grantService = grantService;
        _companyMembershipService = companyMembershipService;
    }

    public string BuildAuthenticationUrl(string griffinBaseUrl, string tokenConsumerUrl)
    {
        LogBuildingAuthUrl(_logger);
        LogAuthUrlBaseUrl(_logger, griffinBaseUrl);
        LogAuthUrlTokenConsumerUrl(_logger, tokenConsumerUrl);

        // DON'T encode the entire URL - Griffin needs to see a valid URL structure.
        // The tokenConsumerUrl already has its returnUrl parameter properly encoded by the caller.
        var finalUrl = $"{griffinBaseUrl.TrimEnd('/')}/authentication?tokenConsumerURL={tokenConsumerUrl}";

        LogAuthUrlConstructed(_logger, finalUrl);
        return finalUrl;
    }

    public async Task<GriffinApiResult<string>> ExchangeTokenAsync(string hashedToken, string griffinBaseUrl, int timeoutSeconds)
    {
        // Griffin's /authentication/claimToken endpoint takes the HASHED token under
        // the 'hash' query parameter. (Note: /authorization/validate and /getClaims
        // use 'token' — they take a JWT, not a hash. Different inputs, different names.)
        // If you change this, also update the regression-guard tests in GriffinServiceTests.cs
        // and the empirical proof rendered by /GriffinDiagnostic's "Live token-exchange
        // comparison" section, which asserts that ?hash= succeeds and ?token= is rejected.
        var url = $"{griffinBaseUrl.TrimEnd('/')}/authentication/claimToken?hash={Uri.EscapeDataString(hashedToken)}";

        LogExchangingToken(_logger);

        var http = await CallGriffinGetAsync(url, timeoutSeconds, GriffinStage.TokenExchange);
        if (!http.Success)
        {
            LogStageCallFailed(_logger, "token exchange", http.Error!.ErrorToken, http.Error.TechnicalDetail);
            _securityLogger.LogAuthenticationFailure("Griffin SSO", http.Error.Host ?? "unknown",
                $"Token exchange {http.Error.ErrorToken}");
            return GriffinApiResult<string>.FailFrom(http);
        }

        var jwt = ExtractTokenFromResponse(http.Value!);
        if (string.IsNullOrWhiteSpace(jwt))
        {
            var err = new GriffinApiError(
                GriffinStage.TokenExchange,
                GriffinErrorCode.EmptyResponse,
                $"claimToken returned a response we could not parse into a JWT. Raw body (first 300 chars): '{TrimForLog(http.Value)}'",
                TryGetHost(url),
                ResponsePreview: TrimForLog(http.Value));
            LogTokenExchangeUnparsable(_logger, err.ErrorToken);
            return GriffinApiResult<string>.Fail(err);
        }

        LogTokenExchangeSuccess(_logger);
        return GriffinApiResult<string>.Ok(jwt);
    }

    public async Task<GriffinApiResult<bool>> ValidateTokenAsync(string token, string griffinBaseUrl, int timeoutSeconds)
    {
        // SECURITY NOTE: Token passed as query parameter per Griffin API protocol.
        // Risk: token may appear in Griffin server access logs. Acceptable for air-gapped deployment.
        var url = $"{griffinBaseUrl.TrimEnd('/')}/authorization/validate?token={Uri.EscapeDataString(token)}";

        LogValidatingToken(_logger);

        var http = await CallGriffinGetAsync(url, timeoutSeconds, GriffinStage.TokenValidation);
        if (!http.Success)
        {
            LogStageCallFailed(_logger, "token validation", http.Error!.ErrorToken, http.Error.TechnicalDetail);
            _securityLogger.LogAuthenticationFailure("Griffin SSO", http.Error.Host ?? "unknown",
                $"Token validation {http.Error.ErrorToken}");
            return GriffinApiResult<bool>.FailFrom(http);
        }

        var body = http.Value!.Trim().Trim('"').Trim();

        // Case-insensitive parse. A clean "false" is NOT an error — it means
        // the token is not valid, and callers treat that as a normal negative.
        if (IsTruthy(body)) return GriffinApiResult<bool>.Ok(true);
        if (IsFalsy(body)) return GriffinApiResult<bool>.Ok(false);

        var err = new GriffinApiError(
            GriffinStage.TokenValidation,
            GriffinErrorCode.UnexpectedResponseShape,
            $"validate returned '{TrimForLog(body)}' — expected boolean true/false/1/0/yes/no",
            TryGetHost(url),
            ResponsePreview: TrimForLog(body));
        LogValidateUnexpectedShape(_logger, err.ErrorToken);
        return GriffinApiResult<bool>.Fail(err);
    }

    public async Task<GriffinApiResult<GriffinClaimsDto>> GetClaimsAsync(string token, string griffinBaseUrl, int timeoutSeconds)
    {
        // SECURITY NOTE: Token passed as query parameter per Griffin API protocol.
        var url = $"{griffinBaseUrl.TrimEnd('/')}/authorization/getClaims?token={Uri.EscapeDataString(token)}";

        LogFetchingClaims(_logger);

        var http = await CallGriffinGetAsync(url, timeoutSeconds, GriffinStage.ClaimsRetrieval);
        if (!http.Success)
        {
            LogStageCallFailed(_logger, "getClaims", http.Error!.ErrorToken, http.Error.TechnicalDetail);
            _securityLogger.LogAuthenticationFailure("Griffin SSO", http.Error.Host ?? "unknown",
                $"Claims retrieval {http.Error.ErrorToken}");
            return GriffinApiResult<GriffinClaimsDto>.FailFrom(http);
        }

        var body = http.Value!.Trim();
        if (body.Length == 0)
        {
            var err = new GriffinApiError(GriffinStage.ClaimsRetrieval, GriffinErrorCode.EmptyResponse,
                "getClaims returned an empty body", TryGetHost(url));
            return GriffinApiResult<GriffinClaimsDto>.Fail(err);
        }

        // Sanity check: getClaims is contractually a JSON object. If the body looks like
        // HTML (a captive-portal/WAF interception page returning 200 with text/html) the
        // JSON parse would still succeed-or-fail confusingly. Catching the obvious HTML
        // case up-front gives a clearer diagnostic for air-gapped admins.
        if (body[0] == '<')
        {
            var err = new GriffinApiError(
                GriffinStage.ClaimsRetrieval,
                GriffinErrorCode.UnexpectedResponseShape,
                $"getClaims body looks like HTML, not JSON — likely a proxy/WAF interception. Body preview: '{TrimForLog(body)}'",
                TryGetHost(url),
                ResponsePreview: TrimForLog(body));
            return GriffinApiResult<GriffinClaimsDto>.Fail(err);
        }

        // Use the flexible Parse factory instead of JsonSerializer.Deserialize directly —
        // it normalizes property names (case + separator agnostic, so snake_case, kebab-case,
        // and LDAP-style aliases all match) AND reports the actual keys present in the body so
        // a missing-field error tells the admin exactly what Griffin DID send. The previous
        // case-only matching produced silent empty fields on any non-casing variation and gave
        // the admin no clue about the actual JSON shape.
        var claims = GriffinClaimsDto.Parse(body, out var presentKeys);
        if (claims == null)
        {
            var err = new GriffinApiError(
                GriffinStage.ClaimsRetrieval,
                GriffinErrorCode.UnreadableResponse,
                $"getClaims body could not be parsed as a JSON object. Body preview: '{TrimForLog(body)}'",
                TryGetHost(url),
                ResponsePreview: TrimForLog(body));
            LogGetClaimsInvalidJson(_logger, err.ErrorToken);
            return GriffinApiResult<GriffinClaimsDto>.Fail(err);
        }

        // Format the actual top-level keys for inclusion in any missing-field error. This is
        // the diagnostic surface Agent 2 identified as the #1 operational gap — when a field
        // is missing, the admin needs to see what Griffin DID emit so they can update either
        // the ADFS claim mapping or the alias list in GriffinClaimsDto.
        var keysSummary = presentKeys.Count == 0
            ? "(none)"
            : string.Join(", ", presentKeys.Select(k => $"'{k}'"));

        if (string.IsNullOrWhiteSpace(claims.EmailAddress))
        {
            var err = new GriffinApiError(
                GriffinStage.ClaimsRetrieval,
                GriffinErrorCode.MissingEmailAddress,
                $"Claims JSON did not contain a recognized email field. "
                + "Aliases tried: EmailAddress / Email / Mail / Upn / UserPrincipalName "
                + "(matching is case- and separator-agnostic). "
                + $"Top-level keys present in response: [{keysSummary}].",
                TryGetHost(url),
                ResponsePreview: TrimForLog(body));
            LogGetClaimsMissingEmail(_logger, err.ErrorToken);
            _securityLogger.LogSecurityThreat("MissingClaims",
                "Griffin token claims missing EmailAddress — possible token tampering or Griffin misconfiguration", "unknown");
            return GriffinApiResult<GriffinClaimsDto>.Fail(err);
        }

        if (string.IsNullOrWhiteSpace(claims.UniqueID))
        {
            var err = new GriffinApiError(
                GriffinStage.ClaimsRetrieval,
                GriffinErrorCode.MissingUniqueId,
                $"Claims JSON did not contain a recognized unique-id field. "
                + "Aliases tried: UniqueID / Uid / Sub / UserId / EmployeeId / SubjectId. "
                + $"Top-level keys present in response: [{keysSummary}].",
                TryGetHost(url),
                ResponsePreview: TrimForLog(body));
            LogGetClaimsMissingUniqueId(_logger, err.ErrorToken);
            return GriffinApiResult<GriffinClaimsDto>.Fail(err);
        }

        LogClaimsRetrieved(_logger, claims.EmailAddress, keysSummary, claims.DisplayName, claims.GivenName, claims.Surname);
        return GriffinApiResult<GriffinClaimsDto>.Ok(claims);
    }

    public async Task<GriffinApiResult<GriffinClaimsDto>> ValidateAndGetClaimsAsync(string token, string griffinBaseUrl, int timeoutSeconds)
    {
        // Compute the hash once and reuse it for both positive and negative cache keys.
        var tokenHash = ComputeSHA256Hash(token);
        var cacheKey = $"griffin_claims_{tokenHash}";
        var negativeCacheKey = $"griffin_invalid_{tokenHash}";

        if (_cache.TryGetValue<GriffinClaimsDto>(cacheKey, out var cachedClaims) && cachedClaims != null)
        {
            LogClaimsCacheHit(_logger);
            return GriffinApiResult<GriffinClaimsDto>.Ok(cachedClaims);
        }

        // Negative cache: if Griffin already told us this token is invalid within the last
        // 60s, return the same error without hammering the Griffin server again. Defends
        // against accidental client-side retry loops AND against an attacker spinning the
        // same bad token to overload Griffin (single-server, often air-gapped).
        if (_cache.TryGetValue<GriffinApiError>(negativeCacheKey, out var cachedNegative) && cachedNegative != null)
        {
            LogClaimsCacheHit(_logger);
            return GriffinApiResult<GriffinClaimsDto>.Fail(cachedNegative);
        }

        LogClaimsCacheMiss(_logger);

        var validateResult = await ValidateTokenAsync(token, griffinBaseUrl, timeoutSeconds);
        if (!validateResult.Success)
            // Transport / parse errors are NOT negative-cached — those should retry.
            // Only Griffin-said-invalid (below) gets the negative cache treatment.
            return GriffinApiResult<GriffinClaimsDto>.FailFrom(validateResult);

        if (!validateResult.Value)
        {
            // Clean negative — Griffin says the token is no longer valid.
            var err = new GriffinApiError(
                GriffinStage.TokenValidation,
                GriffinErrorCode.HttpUnauthorized,
                "Griffin /authorization/validate returned false — the token is no longer valid (likely expired).",
                null,
                HttpStatus: 401);
            _cache.Set(negativeCacheKey, err, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60),
                Priority = CacheItemPriority.Low
            });
            return GriffinApiResult<GriffinClaimsDto>.Fail(err);
        }

        var claimsResult = await GetClaimsAsync(token, griffinBaseUrl, timeoutSeconds);
        if (!claimsResult.Success) return claimsResult;

        _cache.Set(cacheKey, claimsResult.Value, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(2),
            Priority = CacheItemPriority.Normal
        });

        return claimsResult;
    }

    public async Task<GriffinApiResult<ClaimsPrincipal>> AuthenticateUserAsync(string token, GriffinConfig config, string ipAddress)
    {
        // ValidateAndGetClaimsAsync may serve cached claims — but the live IsActive enforcement
        // happens UNCONDITIONALLY at the DB lookup below (lines querying _dbContext.Users).
        // This means a deactivated user's prior cached claims still produce a UserDeactivated
        // failure on the next auth attempt. Don't refactor this without preserving that property.
        var claimsResult = await ValidateAndGetClaimsAsync(token, config.BaseUrl!, config.TimeoutSeconds);
        if (!claimsResult.Success)
        {
            _securityLogger.LogAuthenticationFailure("Griffin SSO", ipAddress,
                $"{claimsResult.Error!.ErrorToken}: {claimsResult.Error.TechnicalDetail}");
            return GriffinApiResult<ClaimsPrincipal>.FailFrom(claimsResult);
        }

        return await BuildPrincipalFromClaimsAsync(claimsResult.Value!, token, ipAddress);
    }

    /// <summary>
    /// Runs the post-Griffin authentication pipeline given pre-resolved claims: DB lookup →
    /// IsActive → role-template backfill → hierarchy load → grant application → ClaimsPrincipal
    /// build. Used by <see cref="AuthenticateUserAsync"/> AND by the /GriffinDiagnostic
    /// "Simulate ADFS Login" diagnostic tool, which lets admins exercise this exact pipeline
    /// against the real DB without making any HTTP calls to Griffin. Pass a non-null
    /// <paramref name="tokenForHash"/> in production (the live token gets SHA256-hashed into a
    /// claim); pass null in simulation mode (a placeholder hash is used instead).
    /// </summary>
    public async Task<GriffinApiResult<ClaimsPrincipal>> BuildPrincipalFromClaimsAsync(
        GriffinClaimsDto griffinClaims, string? tokenForHash, string ipAddress)
    {
        // Defence-in-depth: trim the email locally before the DB compare. The new
        // GriffinClaimsDto.Parse already trims string scalars, but a future refactor or
        // alternative call path (e.g. tests constructing the DTO directly) could bypass that.
        // SQLite's `=` comparison does NOT strip trailing whitespace, so an untrimmed value
        // would silently fail to match the DB row and produce a misleading UserNotRegistered.
        var emailForLookup = (griffinClaims.EmailAddress ?? string.Empty).Trim();
        // Lower-case the search value CLIENT-SIDE with ToLowerInvariant (safe for the ASCII
        // characters that are valid in email per RFC 5321 — Turkish-locale dotless-i issue
        // can never trigger because emails contain no 'I' codepoints that fold differently).
        // The column side below uses .ToLower() which EF Core translates to SQL LOWER()
        // (locale-neutral at the SQL engine level). .ToLowerInvariant() has NO SQL mapping
        // in EF Core and throws "could not be translated" — that was the original 510 bug.
        var emailForLookupLower = emailForLookup.ToLowerInvariant();

        // SECURITY-AUDITED: SAFE — authentication must search across all companies to find user by email.
        // Wrapped in try/catch with a SPECIFIC error code so that EF/SQLite/translation failures
        // surface as GRIFFIN-USERLOOKUP-510 instead of the generic GRIFFIN-USERLOOKUP-900 catch-all
        // at the callback layer — that bare 900 told us nothing about which step actually threw.
        AppUser? user;
        try
        {
            user = await _dbContext.Users
                .IgnoreQueryFilters()
                .Include(u => u.RoleTemplate)
                .Include(u => u.JobType)
                .FirstOrDefaultAsync(u => u.Email.ToLower() == emailForLookupLower);
        }
        // Filter out OperationCanceledException — that's a client-disconnect / IIS-timeout signal,
        // not a Griffin failure. Recording it would fill the diagnostics buffer with noise and
        // mask real errors. Let it propagate to the request-pipeline cancellation path.
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Griffin user lookup query failed for email {Email}", emailForLookup);
            return GriffinApiResult<ClaimsPrincipal>.Fail(new GriffinApiError(
                GriffinStage.UserLookup,
                GriffinErrorCode.UserLookupQueryFailed,
                $"User lookup query threw {ex.GetType().Name}: {ex.Message}"));
        }

        if (user != null && !user.IsActive)
        {
            var err = new GriffinApiError(
                GriffinStage.UserLookup,
                GriffinErrorCode.UserDeactivated,
                $"User {griffinClaims.EmailAddress} exists but IsActive=false");
            _securityLogger.LogAuthenticationFailure(griffinClaims.EmailAddress, ipAddress, "Account deactivated");
            return GriffinApiResult<ClaimsPrincipal>.Fail(err);
        }

        if (user == null)
        {
            // ADFS authenticated successfully but no matching ShiftManager account exists.
            // The callback (Pages/Auth/GriffinCallback.cshtml.cs) decides what to do next
            // based on the FF_ALLOW_USERS_CREATION_VIA_ADFS feature flag:
            //   - Flag ON  → redirect to /Auth/GriffinSignup (admin-approval join request)
            //   - Flag OFF → render refusal page asking the user to contact their officer
            // Either way, we never silently create an account here.
            var err = new GriffinApiError(
                GriffinStage.UserLookup,
                GriffinErrorCode.UserNotRegistered,
                $"User {griffinClaims.EmailAddress} authenticated via Griffin but has no ShiftManager account");
            LogUserNotRegistered(_logger, griffinClaims.EmailAddress);
            _securityLogger.LogAuthenticationFailure(griffinClaims.EmailAddress, ipAddress, "ADFS-authenticated user has no ShiftManager account");
            return GriffinApiResult<ClaimsPrincipal>.Fail(err);
        }

        // Login-time backfill safety net: if user has no RoleTemplate, derive from Role + JobType.
        // Wrapped: SaveChangesAsync hits CompanyIdInterceptor + EF change-tracker, both of which
        // have failure modes worth surfacing distinctly (e.g., FK violation on RoleTemplateId).
        if (user.RoleTemplateId == null)
        {
            try
            {
                var templateKey = MapUserRoleToRoleTemplateKey(user.Role, user.JobType?.Name);
                var template = await _dbContext.RoleTemplates.IgnoreQueryFilters().FirstOrDefaultAsync(rt => rt.Key == templateKey);
                if (template != null)
                {
                    user.RoleTemplateId = template.Id;
                    user.RoleTemplate = template;
                    if (template.DerivedUserRole.HasValue)
                        user.Role = template.DerivedUserRole.Value;
                    await _dbContext.SaveChangesAsync();
                    LogBackfilledRoleTemplate(_logger, template.Id, user.Id);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Griffin role-template backfill failed for user {UserId}", user.Id);
                return GriffinApiResult<ClaimsPrincipal>.Fail(new GriffinApiError(
                    GriffinStage.UserLookup,
                    GriffinErrorCode.RoleTemplateBackfillFailed,
                    $"Role-template backfill threw {ex.GetType().Name}: {ex.Message}"));
            }
        }

        UserHierarchyContext? hierarchyContext;
        try
        {
            hierarchyContext = await _hierarchyService.GetUserHierarchyContextAsync(user.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Griffin hierarchy load failed for user {UserId}", user.Id);
            return GriffinApiResult<ClaimsPrincipal>.Fail(new GriffinApiError(
                GriffinStage.UserLookup,
                GriffinErrorCode.HierarchyLoadFailed,
                $"Hierarchy load for user {user.Id} threw {ex.GetType().Name}: {ex.Message}"));
        }

        // Defensive guard: workforce user (CompanyId > 0) with a role template AND null
        // hierarchy context indicates the user's Company → Molecule → Area → Project chain
        // is broken (orphan FK from soft-deleted intermediate row, or seed-data corruption).
        // The 2026-05-11 audit found that proceeding to ApplyAutoGrantsAsync with null path
        // fields silently creates grants with MoleculeId/AreaId/ProjectId = null — interpreted
        // as GLOBAL scope = silent over-granting.
        //
        // Guard fires only when BOTH conditions hold:
        //   1. user.RoleTemplateId.HasValue → ApplyAutoGrantsAsync WILL run (otherwise it's
        //      skipped at line `if (user.RoleTemplateId.HasValue)` below and no over-grant
        //      can occur)
        //   2. workforce user (CompanyId > 0, no DepartmentId) → hierarchy null is unexpected
        //      and signals a broken chain
        //
        // Users without a role template (RoleTemplateId == null) — sanitisation/CR/LF tests,
        // seed-data fixtures, brand-new accounts pre-backfill — fall through gracefully
        // because there's no grant phase to over-grant. The audit's defensive logging in
        // ApplyAutoGrantsAsync covers the residual "template deleted via raw SQL" case.
        if (hierarchyContext == null
            && user.CompanyId > 0
            && !user.DepartmentId.HasValue
            && user.RoleTemplateId.HasValue)
        {
            _logger.LogError(
                "Griffin auth: user {UserId} (CompanyId={CompanyId}, RoleTemplateId={TemplateId}) " +
                "returned null hierarchy context. Likely orphan FK in Company→Molecule→Area→Project " +
                "chain. Refusing login to avoid over-granting.",
                user.Id, user.CompanyId, user.RoleTemplateId);
            return GriffinApiResult<ClaimsPrincipal>.Fail(new GriffinApiError(
                GriffinStage.UserLookup,
                GriffinErrorCode.HierarchyLoadFailed,
                $"User {user.Id} has CompanyId={user.CompanyId} and a role template but the hierarchy " +
                $"chain returned null. This usually means a Molecule, Area, or Project the user " +
                $"belongs to was deleted. Restore the broken hierarchy link or move the user to a " +
                $"valid company."));
        }

        if (user.RoleTemplateId.HasValue)
        {
            try
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
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Griffin grant application failed for user {UserId} template {TemplateId}",
                    user.Id, user.RoleTemplateId);
                return GriffinApiResult<ClaimsPrincipal>.Fail(new GriffinApiError(
                    GriffinStage.UserLookup,
                    GriffinErrorCode.GrantApplicationFailed,
                    $"Grant application for user {user.Id} threw {ex.GetType().Name}: {ex.Message}"));
            }
        }

        // SECURITY: every value flowing into a Claim is sanitised — neither Griffin nor our
        // own DB can inject CR/LF (log injection), NUL bytes (log truncation), or oversize
        // strings into the auth principal. Defense-in-depth: input validation on the Profile
        // edit pages already constrains DB-side values, but we re-sanitise here so a corrupt
        // row can never produce a malformed claim.
        //
        // CLAIM-SOURCING POLICY (2026-05-17):
        //   • ClaimTypes.Name        ← user.DisplayName (DB)          — see note below (a)
        //   • ClaimTypes.GivenName   ← user.PreferredName ?? first-token-of-DisplayName (DB)
        //   • ClaimTypes.Email       ← user.Email (DB)                — DB casing is canonical
        //   • ClaimTypes.NameIdentifier / Griffin:UniqueID ← Griffin (external identity)
        //
        // (a) Why DB-over-Griffin: AppUser fields are user-editable inside the app (e.g. a
        //     user named "Gabriel" sets PreferredName = "Gabi", or fixes their display name's
        //     spelling). Re-sourcing from Griffin on every login would silently overwrite those
        //     edits in the active session. The external identifier (UniqueID/NameIdentifier)
        //     still comes from Griffin so audit trails stay linked to ADFS.
        //
        // Fallback chain for Name: DisplayName → PreferredName → Email → UniqueID, so we never
        // set an empty Name claim even on a corrupt user row.
        //
        // Wrapped: a Claim ctor null/empty failure or any pathological string here would
        // otherwise bubble up as the generic GRIFFIN-USERLOOKUP-900 we just retired.
        ClaimsPrincipal principal;
        try
        {
        var dbDisplayName = SanitizeClaimString(user.DisplayName);
        var dbPreferredName = SanitizeClaimString(user.PreferredName ?? string.Empty);
        var dbEmail = SanitizeClaimString(user.Email, maxLength: 254); // RFC 5321 max
        var safeUniqueId = SanitizeClaimString(griffinClaims.UniqueID, maxLength: 128);

        // Name claim — DB DisplayName, with safe fallbacks so we never emit empty.
        var nameForClaim = dbDisplayName;
        if (string.IsNullOrEmpty(nameForClaim)) nameForClaim = dbPreferredName;
        if (string.IsNullOrEmpty(nameForClaim)) nameForClaim = dbEmail;
        if (string.IsNullOrEmpty(nameForClaim)) nameForClaim = safeUniqueId;

        // GivenName claim — PreferredName beats first token of DisplayName so "Gabriel" can
        // self-identify as "Gabi" in the dashboard greeting, sidebar, page title, etc.
        var givenNameForClaim = !string.IsNullOrEmpty(dbPreferredName)
            ? dbPreferredName
            : (dbDisplayName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? dbDisplayName);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, nameForClaim),
            new Claim(ClaimTypes.Email, dbEmail),
            new Claim(ClaimTypes.GivenName, givenNameForClaim),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("CompanyId", user.CompanyId.ToString(CultureInfo.InvariantCulture)),
            new Claim("AuthMethod", "Griffin"),
            new Claim("Griffin:UniqueID", safeUniqueId),
            // HIGH-007: store hashed token reference instead of raw token in claims.
            // The /GriffinDiagnostic "Simulate ADFS Login" path passes tokenForHash=null
            // because no real token exists — record a fixed sentinel hash so the claim is
            // still populated and downstream code never sees a null Griffin:TokenHash value.
            new Claim("Griffin:TokenHash",
                string.IsNullOrEmpty(tokenForHash) ? "simulated" : ComputeSHA256Hash(tokenForHash)),
            new Claim("Griffin:AuthTime", FormatClaim(griffinClaims.IssuedAt)),
            new Claim("AuthTimestamp", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture))
        };

        if (!string.IsNullOrWhiteSpace(user.AvatarFileName))
            claims.Add(new Claim("AvatarFileName", user.AvatarFileName));

        if (user.RoleTemplate != null)
            claims.Add(new Claim("RoleTemplateKey", user.RoleTemplate.Key));

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

        // Multi-company: expose the user's active membership company-id set so TenantResolver can
        // validate a member's active-company switch synchronously (no DB call in the resolver).
        var memberships = await _companyMembershipService.GetMembershipsAsync(user.Id);
        if (memberships.Count > 1)
        {
            claims.Add(new Claim("MemberCompanyIds",
                string.Join(",", memberships.Select(m => m.CompanyId).Distinct())));
        }

            var identity = new ClaimsIdentity(claims, "Griffin");
            principal = new ClaimsPrincipal(identity);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Griffin ClaimsPrincipal build failed for user {UserId}", user.Id);
            return GriffinApiResult<ClaimsPrincipal>.Fail(new GriffinApiError(
                GriffinStage.UserLookup,
                GriffinErrorCode.ClaimsPrincipalBuildFailed,
                $"ClaimsPrincipal build for user {user.Id} threw {ex.GetType().Name}: {ex.Message}"));
        }

        LogAuthenticationSuccess(_logger, user.Id, user.Email);
        _securityLogger.LogAuthenticationSuccess(user.Id, user.Email, user.Role.ToString(), ipAddress);

        return GriffinApiResult<ClaimsPrincipal>.Ok(principal);
    }

    // ------------------------------------------------------------------
    // Shared HTTP helper — every Griffin call funnels through here so the
    // error classification is consistent across all three endpoints.
    // ------------------------------------------------------------------
    private async Task<GriffinApiResult<string>> CallGriffinGetAsync(string url, int timeoutSeconds, GriffinStage stage)
    {
        var host = TryGetHost(url);
        HttpResponseMessage response;
        try
        {
            using var client = _httpClientFactory.CreateClient("GriffinClient");
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            response = await client.GetAsync(url);
        }
        catch (TaskCanceledException ex)
        {
            return GriffinApiResult<string>.Fail(new GriffinApiError(
                stage, GriffinErrorCode.NetworkTimeout,
                FormattableString.Invariant($"Timed out after {timeoutSeconds}s calling {StripQuery(url)}. {ex.Message}"), host));
        }
        catch (HttpRequestException ex) when (TryFindSocketError(ex, out var sockErr))
        {
            var code = sockErr switch
            {
                SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain
                    => GriffinErrorCode.NetworkDnsFailure,
                SocketError.ConnectionRefused
                    => GriffinErrorCode.NetworkConnectionRefused,
                SocketError.TimedOut
                    => GriffinErrorCode.NetworkTimeout,
                _ => GriffinErrorCode.NetworkOther,
            };
            return GriffinApiResult<string>.Fail(new GriffinApiError(
                stage, code,
                $"Socket error {sockErr} calling {StripQuery(url)}: {ex.Message}", host));
        }
        catch (HttpRequestException ex) when (HasInner<AuthenticationException>(ex))
        {
            return GriffinApiResult<string>.Fail(new GriffinApiError(
                stage, GriffinErrorCode.NetworkSslError,
                $"SSL/TLS handshake failed with {host}: {ex.Message}", host));
        }
        catch (HttpRequestException ex)
        {
            return GriffinApiResult<string>.Fail(new GriffinApiError(
                stage, GriffinErrorCode.NetworkOther,
                $"HTTP request to {StripQuery(url)} failed: {ex.Message}", host));
        }
        catch (Exception ex)
        {
            LogUnhandledHttpException(_logger, ex, url);
            return GriffinApiResult<string>.Fail(new GriffinApiError(
                stage, GriffinErrorCode.UnhandledException,
                $"Unhandled {ex.GetType().Name} calling {StripQuery(url)}: {ex.Message}", host));
        }

        var status = (int)response.StatusCode;
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync();
            // Strip UTF-8 BOM if Griffin (or a Windows-side proxy) prepends one. Without this,
            // a BOM-prefixed JWT comes through as "﻿eyJ..." which Griffin then rejects with
            // a confusing 400 — and the BOM would also break ExtractTokenFromResponse's JSON
            // path (System.Text.Json's strict mode doesn't tolerate BOM at position 0 in some
            // older runtime versions).
            if (body.Length > 0 && body[0] == '﻿') body = body[1..];
        }
        catch (Exception ex)
        {
            return GriffinApiResult<string>.Fail(new GriffinApiError(
                stage, GriffinErrorCode.UnreadableResponse,
                $"Could not read response body from {StripQuery(url)}: {ex.Message}", host, status));
        }

        if (!response.IsSuccessStatusCode)
        {
            var code = status switch
            {
                400 => GriffinErrorCode.HttpBadRequest,
                401 => GriffinErrorCode.HttpUnauthorized,
                403 => GriffinErrorCode.HttpForbidden,
                404 => GriffinErrorCode.HttpNotFound,
                >= 500 and <= 599 => GriffinErrorCode.HttpServerError,
                _ => GriffinErrorCode.HttpOther,
            };
            return GriffinApiResult<string>.Fail(new GriffinApiError(
                stage, code,
                FormattableString.Invariant($"HTTP {status} from {StripQuery(url)}. Body: '{TrimForLog(body)}'"),
                host, status, ResponsePreview: TrimForLog(body)));
        }

        return GriffinApiResult<string>.Ok(body);
    }

    // ------------------------------------------------------------------
    // Response parsing — tolerates three common shapes for the JWT body:
    //   1. Raw text:          eyJhbGc...zzz
    //   2. JSON-quoted string: "eyJhbGc...zzz"
    //   3. JSON object:        {"token":"eyJ..."} or {"jwt":"..."} or {"accessToken":"..."}
    // Anything that survives as a non-empty, dot-containing string is
    // accepted — validation happens at the next hop (validate/getClaims),
    // which gives Griffin the final say on whether it's a real JWT.
    // ------------------------------------------------------------------
    internal static string? ExtractTokenFromResponse(string rawBody)
    {
        var trimmed = rawBody.Trim();
        if (trimmed.Length == 0) return null;

        // Quick path — not JSON, so just strip surrounding quotes if any.
        // Apply the same JWT-shape gate as the fallback so a captive-portal HTML/text
        // response that happens to start with letters can't slip through and be passed
        // downstream as the "JWT".
        if (trimmed[0] != '{' && trimmed[0] != '[')
        {
            var plain = trimmed.Trim('"').Trim();
            if (plain.Length == 0) return null;
            return LooksLikeJwt(plain) ? plain : null;
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind == JsonValueKind.String)
            {
                var s = doc.RootElement.GetString();
                return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
            }
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in new[] { "token", "Token", "jwt", "JWT", "accessToken", "access_token", "AccessToken" })
                {
                    if (doc.RootElement.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
                    {
                        var s = prop.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Not valid JSON — fall through to return the trimmed body as-is.
        }

        // Last resort: return the trimmed body with surrounding quotes removed —
        // BUT only if it's structurally JWT-shaped (RFC 7519 § 3 mandates exactly 3
        // dot-separated base64url segments). This guards against a misconfigured WAF
        // returning HTML 200 ("Welcome to nginx!") that would otherwise be passed
        // downstream as the "JWT" and produce a confusing HTTP 400 at validate.
        var fallback = trimmed.Trim('"').Trim();
        if (fallback.Length == 0) return null;
        return LooksLikeJwt(fallback) ? fallback : null;
    }

    // RFC 7519 § 3: a JWS Compact Serialization is exactly three dot-separated segments
    // (header.payload.signature). Each segment is base64url. This is the cheapest
    // structural check we can do without parsing the cryptographic envelope.
    internal static bool LooksLikeJwt(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        var parts = s.Split('.');
        if (parts.Length != 3) return false;
        foreach (var p in parts)
        {
            if (p.Length == 0) return false;
            foreach (var c in p)
            {
                // base64url alphabet: A-Z a-z 0-9 - _   (unpadded; '=' padding allowed by some impls)
                if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '='))
                    return false;
            }
        }
        return true;
    }

    internal static bool IsTruthy(string s) =>
        s.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        s == "1" ||
        s.Equals("yes", StringComparison.OrdinalIgnoreCase);

    internal static bool IsFalsy(string s) =>
        s.Equals("false", StringComparison.OrdinalIgnoreCase) ||
        s == "0" ||
        s.Equals("no", StringComparison.OrdinalIgnoreCase);

    private static bool TryFindSocketError(Exception ex, out SocketError code)
    {
        for (var e = ex; e != null; e = e.InnerException!)
        {
            if (e is SocketException sockEx)
            {
                code = sockEx.SocketErrorCode;
                return true;
            }
            if (e.InnerException == null) break;
        }
        code = default;
        return false;
    }

    private static bool HasInner<T>(Exception ex) where T : Exception
    {
        for (var e = ex.InnerException; e != null; e = e.InnerException)
            if (e is T) return true;
        return false;
    }

    private static string? TryGetHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : null;

    private static string TrimForLog(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var oneLine = s.Replace('\r', ' ').Replace('\n', ' ');
        return oneLine.Length > 300 ? oneLine[..300] + "..." : oneLine;
    }

    // JWT-spec claims (iat/nbf/exp/aud/iss) arrive on GriffinClaimsDto as JsonElement?
    // because Griffin's response shape varies (number vs string vs array). This helper
    // normalises to a string for places that need one (auth-cookie claim values, logs).
    private static string FormatClaim(JsonElement? element)
        => element is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null }
            ? element.Value.ToString()
            : string.Empty;

    // SECURITY: Removes the query string from a URL before embedding it in user-facing
    // error messages or non-secure logs. Griffin URLs carry the JWT in `?token=...` /
    // `?hash=...` query parameters, so logging the full URL leaks the token. The Path
    // portion alone identifies the endpoint (e.g. "/authorization/getClaims") for
    // debugging without revealing credentials.
    private static string StripQuery(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return uri.GetLeftPart(UriPartial.Path);
        // Fallback for malformed URLs: cut at the first '?'.
        var qIdx = url.IndexOf('?', StringComparison.Ordinal);
        return qIdx >= 0 ? url[..qIdx] : url;
    }

    // Sanitise an externally-supplied string before it becomes a Claim value.
    // Griffin returns DisplayName/GivenName/Surname unmodified from ADFS — a malicious
    // ADFS response (or a misconfigured user record with a newline in the name) could
    // otherwise inject log-line terminators, NUL bytes (truncate downstream sinks),
    // or arbitrarily long values into our security principal. Caps length, drops
    // control chars, normalises CR/LF to space.
    private static string SanitizeClaimString(string? input, int maxLength = 256)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var sb = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (c == '\0') continue;                      // drop NUL — log truncation guard
            if (c == '\r' || c == '\n') { sb.Append(' '); continue; }  // log-injection guard
            if (char.IsControl(c) && c != '\t') continue; // drop other control chars
            sb.Append(c);
        }
        var s = sb.ToString().Trim();
        return s.Length > maxLength ? s[..maxLength] : s;
    }

    private static string MapUserRoleToRoleTemplateKey(UserRole role, string? jobTypeName)
        => Helpers.RoleTemplateMapper.MapUserRoleToRoleTemplateKey(role, jobTypeName);

    // Allocation-free SHA256 helper — called twice per Griffin login (cache key + token-hash claim).
    // SHA256.HashData (added in .NET 5) avoids the per-call SHA256 instance allocation that
    // SHA256.Create() requires.
    private static string ComputeSHA256Hash(string input)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
}
