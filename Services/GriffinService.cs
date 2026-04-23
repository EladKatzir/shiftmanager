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
public class GriffinService : IGriffinService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AppDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly ISecurityLogger _securityLogger;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<GriffinService> _logger;
    private readonly IHierarchyService _hierarchyService;
    private readonly IGrantService _grantService;

    public GriffinService(
        IHttpClientFactory httpClientFactory,
        AppDbContext dbContext,
        IMemoryCache cache,
        ISecurityLogger securityLogger,
        IAuditLogService auditLogService,
        ILogger<GriffinService> logger,
        IHierarchyService hierarchyService,
        IGrantService grantService)
    {
        _httpClientFactory = httpClientFactory;
        _dbContext = dbContext;
        _cache = cache;
        _securityLogger = securityLogger;
        _auditLogService = auditLogService;
        _logger = logger;
        _hierarchyService = hierarchyService;
        _grantService = grantService;
    }

    public string BuildAuthenticationUrl(string griffinBaseUrl, string tokenConsumerUrl)
    {
        _logger.LogDebug("Building Griffin authentication URL:");
        _logger.LogDebug("  - Griffin BaseUrl: {BaseUrl}", griffinBaseUrl);
        _logger.LogDebug("  - TokenConsumerUrl (NOT encoded): {TokenConsumerUrl}", tokenConsumerUrl);

        // DON'T encode the entire URL - Griffin needs to see a valid URL structure.
        // The tokenConsumerUrl already has its returnUrl parameter properly encoded by the caller.
        var finalUrl = $"{griffinBaseUrl.TrimEnd('/')}/authentication?tokenConsumerURL={tokenConsumerUrl}";

        _logger.LogInformation("Griffin authentication URL constructed: {FinalUrl}", finalUrl);
        return finalUrl;
    }

    public async Task<GriffinApiResult<string>> ExchangeTokenAsync(string hashedToken, string griffinBaseUrl, int timeoutSeconds)
    {
        // Griffin's /authentication/claimToken endpoint expects the hashed token
        // under the 'token' query parameter (same convention as /validate and /getClaims).
        var url = $"{griffinBaseUrl.TrimEnd('/')}/authentication/claimToken?token={Uri.EscapeDataString(hashedToken)}";

        _logger.LogDebug("Exchanging Griffin hashed token for JWT");

        var http = await CallGriffinGetAsync(url, timeoutSeconds, GriffinStage.TokenExchange);
        if (!http.Success)
        {
            _logger.LogWarning("Griffin token exchange failed [{ErrorToken}]: {Detail}",
                http.Error!.ErrorToken, http.Error.TechnicalDetail);
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
            _logger.LogWarning("Griffin token exchange returned unparsable body [{ErrorToken}]", err.ErrorToken);
            return GriffinApiResult<string>.Fail(err);
        }

        _logger.LogDebug("Griffin token exchange successful, received JWT");
        return GriffinApiResult<string>.Ok(jwt);
    }

    public async Task<GriffinApiResult<bool>> ValidateTokenAsync(string token, string griffinBaseUrl, int timeoutSeconds)
    {
        // SECURITY NOTE: Token passed as query parameter per Griffin API protocol.
        // Risk: token may appear in Griffin server access logs. Acceptable for air-gapped deployment.
        var url = $"{griffinBaseUrl.TrimEnd('/')}/authorization/validate?token={Uri.EscapeDataString(token)}";

        _logger.LogDebug("Validating Griffin token (masked: ***)");

        var http = await CallGriffinGetAsync(url, timeoutSeconds, GriffinStage.TokenValidation);
        if (!http.Success)
        {
            _logger.LogWarning("Griffin token validation call failed [{ErrorToken}]: {Detail}",
                http.Error!.ErrorToken, http.Error.TechnicalDetail);
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
        _logger.LogWarning("Griffin validate returned unexpected shape [{ErrorToken}]", err.ErrorToken);
        return GriffinApiResult<bool>.Fail(err);
    }

    public async Task<GriffinApiResult<GriffinClaimsDto>> GetClaimsAsync(string token, string griffinBaseUrl, int timeoutSeconds)
    {
        // SECURITY NOTE: Token passed as query parameter per Griffin API protocol.
        var url = $"{griffinBaseUrl.TrimEnd('/')}/authorization/getClaims?token={Uri.EscapeDataString(token)}";

        _logger.LogDebug("Fetching Griffin claims (token masked: ***)");

        var http = await CallGriffinGetAsync(url, timeoutSeconds, GriffinStage.ClaimsRetrieval);
        if (!http.Success)
        {
            _logger.LogWarning("Griffin getClaims call failed [{ErrorToken}]: {Detail}",
                http.Error!.ErrorToken, http.Error.TechnicalDetail);
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

        GriffinClaimsDto? claims;
        try
        {
            claims = JsonSerializer.Deserialize<GriffinClaimsDto>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            var err = new GriffinApiError(
                GriffinStage.ClaimsRetrieval,
                GriffinErrorCode.UnreadableResponse,
                $"getClaims body was not valid JSON: {ex.Message}. Body preview: '{TrimForLog(body)}'",
                TryGetHost(url),
                ResponsePreview: TrimForLog(body));
            _logger.LogWarning("Griffin getClaims returned invalid JSON [{ErrorToken}]", err.ErrorToken);
            return GriffinApiResult<GriffinClaimsDto>.Fail(err);
        }

        if (claims == null)
        {
            var err = new GriffinApiError(GriffinStage.ClaimsRetrieval, GriffinErrorCode.EmptyResponse,
                "getClaims deserialized to null", TryGetHost(url));
            return GriffinApiResult<GriffinClaimsDto>.Fail(err);
        }

        if (string.IsNullOrWhiteSpace(claims.EmailAddress))
        {
            var err = new GriffinApiError(
                GriffinStage.ClaimsRetrieval,
                GriffinErrorCode.MissingEmailAddress,
                "Claims JSON did not contain a non-empty EmailAddress field",
                TryGetHost(url));
            _logger.LogWarning("Griffin getClaims missing EmailAddress [{ErrorToken}]", err.ErrorToken);
            _securityLogger.LogSecurityThreat("MissingClaims",
                "Griffin token claims missing EmailAddress — possible token tampering or Griffin misconfiguration", "unknown");
            return GriffinApiResult<GriffinClaimsDto>.Fail(err);
        }

        if (string.IsNullOrWhiteSpace(claims.UniqueID))
        {
            var err = new GriffinApiError(
                GriffinStage.ClaimsRetrieval,
                GriffinErrorCode.MissingUniqueId,
                "Claims JSON did not contain a non-empty UniqueID field",
                TryGetHost(url));
            _logger.LogWarning("Griffin getClaims missing UniqueID [{ErrorToken}]", err.ErrorToken);
            return GriffinApiResult<GriffinClaimsDto>.Fail(err);
        }

        _logger.LogDebug("Griffin claims retrieved for EmailAddress: {EmailAddress}", claims.EmailAddress);
        return GriffinApiResult<GriffinClaimsDto>.Ok(claims);
    }

    public async Task<GriffinApiResult<GriffinClaimsDto>> ValidateAndGetClaimsAsync(string token, string griffinBaseUrl, int timeoutSeconds)
    {
        var cacheKey = $"griffin_claims_{ComputeSHA256Hash(token)}";
        if (_cache.TryGetValue<GriffinClaimsDto>(cacheKey, out var cachedClaims) && cachedClaims != null)
        {
            _logger.LogDebug("Griffin claims cache hit");
            return GriffinApiResult<GriffinClaimsDto>.Ok(cachedClaims);
        }

        _logger.LogDebug("Griffin claims cache miss, calling API");

        var validateResult = await ValidateTokenAsync(token, griffinBaseUrl, timeoutSeconds);
        if (!validateResult.Success)
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
        var claimsResult = await ValidateAndGetClaimsAsync(token, config.BaseUrl!, config.TimeoutSeconds);
        if (!claimsResult.Success)
        {
            _securityLogger.LogAuthenticationFailure("Griffin SSO", ipAddress,
                $"{claimsResult.Error!.ErrorToken}: {claimsResult.Error.TechnicalDetail}");
            return GriffinApiResult<ClaimsPrincipal>.FailFrom(claimsResult);
        }

        var griffinClaims = claimsResult.Value!;

        // SECURITY-AUDITED: SAFE — authentication must search across all companies to find user by email
        var user = await _dbContext.Users
            .IgnoreQueryFilters()
            .Include(u => u.RoleTemplate)
            .Include(u => u.JobType)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == griffinClaims.EmailAddress.ToLower());

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
            if (!config.AutoProvisionUsers)
            {
                var err = new GriffinApiError(
                    GriffinStage.UserLookup,
                    GriffinErrorCode.UserNotRegistered,
                    $"User {griffinClaims.EmailAddress} not found and auto-provisioning is disabled");
                _logger.LogInformation("Griffin user {Email} not found and auto-provisioning disabled", griffinClaims.EmailAddress);
                _securityLogger.LogAuthenticationFailure(griffinClaims.EmailAddress, ipAddress, "User not found and auto-provisioning disabled");
                return GriffinApiResult<ClaimsPrincipal>.Fail(err);
            }

            user = await AutoProvisionUserAsync(griffinClaims, config);
            if (user == null)
            {
                var err = new GriffinApiError(
                    GriffinStage.UserProvisioning,
                    GriffinErrorCode.AutoProvisionFailed,
                    $"AutoProvisionUserAsync returned null for {griffinClaims.EmailAddress} — check server logs for DB error");
                _securityLogger.LogAuthenticationFailure(griffinClaims.EmailAddress, ipAddress, "Auto-provisioning failed");
                return GriffinApiResult<ClaimsPrincipal>.Fail(err);
            }
        }

        // Login-time backfill safety net: if user has no RoleTemplate, derive from Role + JobType
        if (user.RoleTemplateId == null)
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
                _logger.LogInformation("Backfilled RoleTemplateId={TemplateId} for Griffin user {UserId}", template.Id, user.Id);
            }
        }

        var hierarchyContext = await _hierarchyService.GetUserHierarchyContextAsync(user.Id);

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
            new Claim(ClaimTypes.Name, griffinClaims.DisplayName),
            new Claim(ClaimTypes.Email, griffinClaims.EmailAddress),
            new Claim(ClaimTypes.GivenName, griffinClaims.GivenName),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("CompanyId", user.CompanyId.ToString()),
            new Claim("AuthMethod", "Griffin"),
            new Claim("Griffin:UniqueID", griffinClaims.UniqueID),
            // HIGH-007: store hashed token reference instead of raw token in claims
            new Claim("Griffin:TokenHash", ComputeSHA256Hash(token)),
            new Claim("Griffin:AuthTime", griffinClaims.IssuedAt),
            new Claim("AuthTimestamp", DateTime.UtcNow.ToString("o"))
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

        var identity = new ClaimsIdentity(claims, "Griffin");
        var principal = new ClaimsPrincipal(identity);

        _logger.LogInformation("Griffin authentication successful for user {UserId} ({Email})", user.Id, user.Email);
        _securityLogger.LogAuthenticationSuccess(user.Id, user.Email, user.Role.ToString(), ipAddress);

        return GriffinApiResult<ClaimsPrincipal>.Ok(principal);
    }

    private async Task<AppUser?> AutoProvisionUserAsync(GriffinClaimsDto griffinClaims, GriffinConfig config)
    {
        try
        {
            var user = new AppUser
            {
                CompanyId = config.CompanyId,
                Email = griffinClaims.EmailAddress,
                DisplayName = griffinClaims.DisplayName,
                Role = config.DefaultProvisionedRole,
                RoleTemplateId = config.DefaultProvisionedRoleTemplateId,
                IsActive = true,
                PasswordHash = Array.Empty<byte>(),
                PasswordSalt = Array.Empty<byte>()
            };

            if (config.DefaultProvisionedRoleTemplateId.HasValue)
            {
                var template = await _dbContext.RoleTemplates.FindAsync(config.DefaultProvisionedRoleTemplateId.Value);
                if (template?.DerivedUserRole.HasValue == true)
                    user.Role = template.DerivedUserRole.Value;
            }

            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Auto-provisioned Griffin user: {Email} with role {Role}, template {TemplateId}",
                user.Email, user.Role, user.RoleTemplateId);
            _securityLogger.LogSensitiveDataAccess(user.Id, "AppUser", "AutoProvision via Griffin SSO");
            _logger.LogWarning("Griffin auto-provisioned user {Email} has no molecule/hierarchy placement. " +
                "Owner or Manager must assign placement before user can access operational features (D-06).",
                user.Email);

            await _auditLogService.LogSystemActionAsync(
                "GriffinAutoProvision",
                "AppUser",
                user.Id,
                $"Auto-provisioned Griffin user: {user.Email} — PENDING PLACEMENT (no molecule/hierarchy assigned)",
                $"Role={user.Role}, CompanyId={user.CompanyId}, EmailAddress={griffinClaims.EmailAddress}");

            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to auto-provision user for {EmailAddress}", griffinClaims.EmailAddress);
            _securityLogger.LogSecurityThreat("AutoProvisionError",
                $"Failed to auto-provision Griffin user {griffinClaims.EmailAddress}: {ex.Message}", null);
            return null;
        }
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
                $"Timed out after {timeoutSeconds}s calling {url}. {ex.Message}", host));
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
                $"Socket error {sockErr} calling {url}: {ex.Message}", host));
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
                $"HTTP request to {url} failed: {ex.Message}", host));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception calling Griffin endpoint {Url}", url);
            return GriffinApiResult<string>.Fail(new GriffinApiError(
                stage, GriffinErrorCode.UnhandledException,
                $"Unhandled {ex.GetType().Name} calling {url}: {ex.Message}", host));
        }

        var status = (int)response.StatusCode;
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            return GriffinApiResult<string>.Fail(new GriffinApiError(
                stage, GriffinErrorCode.UnreadableResponse,
                $"Could not read response body from {url}: {ex.Message}", host, status));
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
                $"HTTP {status} from {url}. Body: '{TrimForLog(body)}'",
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
        if (trimmed[0] != '{' && trimmed[0] != '[')
        {
            var plain = trimmed.Trim('"').Trim();
            return plain.Length == 0 ? null : plain;
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

        // Last resort: return the trimmed body with surrounding quotes removed.
        var fallback = trimmed.Trim('"').Trim();
        return fallback.Length == 0 ? null : fallback;
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

    private static string MapUserRoleToRoleTemplateKey(UserRole role, string? jobTypeName)
        => Helpers.RoleTemplateMapper.MapUserRoleToRoleTemplateKey(role, jobTypeName);

    private string ComputeSHA256Hash(string input)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }
}
