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

        // DON'T encode the entire URL - Griffin needs to see a valid URL structure!
        // The tokenConsumerUrl already has its returnUrl parameter properly encoded by the caller.
        // Encoding the entire URL would make it unrecognizable to Griffin's validation.
        var finalUrl = $"{griffinBaseUrl.TrimEnd('/')}/authentication?tokenConsumerURL={tokenConsumerUrl}";

        _logger.LogInformation("Griffin authentication URL constructed: {FinalUrl}", finalUrl);

        return finalUrl;
    }

    public async Task<bool> ValidateTokenAsync(string token, string griffinBaseUrl, int timeoutSeconds)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            // SECURITY NOTE: Token passed as query parameter per Griffin API protocol.
            // Risk: token may appear in Griffin server access logs. Acceptable for air-gapped deployment.
            // If Griffin API supports it in the future, prefer passing token via Authorization header.
            var url = $"{griffinBaseUrl.TrimEnd('/')}/authorization/validate?token={Uri.EscapeDataString(token)}";

            _logger.LogDebug("Validating Griffin token (masked: ***)");

            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Griffin token validation failed with status {StatusCode}", response.StatusCode);
                _securityLogger.LogAuthenticationFailure("Griffin SSO", "unknown", $"Token validation HTTP {response.StatusCode}");
                return false;
            }

            var content = await response.Content.ReadAsStringAsync();
            var isValid = content.Trim().Trim('"') == "true";

            _logger.LogDebug("Griffin token validation result: {IsValid}", isValid);

            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating Griffin token");
            _securityLogger.LogSecurityThreat("TokenValidationError", $"Griffin token validation threw: {ex.Message}", "unknown");
            return false;
        }
    }

    public async Task<GriffinClaimsDto?> GetClaimsAsync(string token, string griffinBaseUrl, int timeoutSeconds)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            // SECURITY NOTE: Token passed as query parameter per Griffin API protocol.
            // Risk: token may appear in Griffin server access logs. Acceptable for air-gapped deployment.
            var url = $"{griffinBaseUrl.TrimEnd('/')}/authorization/getClaims?token={Uri.EscapeDataString(token)}";

            _logger.LogDebug("Fetching Griffin claims (token masked: ***)");

            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Griffin getClaims failed with status {StatusCode}", response.StatusCode);
                _securityLogger.LogAuthenticationFailure("Griffin SSO", "unknown", $"Claims retrieval HTTP {response.StatusCode}");
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var claims = JsonSerializer.Deserialize<GriffinClaimsDto>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (claims != null)
            {
                _logger.LogDebug("Griffin claims retrieved for UPN: {UPN}", claims.UPN);
            }

            return claims;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Griffin claims");
            _securityLogger.LogSecurityThreat("ClaimsRetrievalError", $"Griffin claims retrieval threw: {ex.Message}", "unknown");
            return null;
        }
    }

    public async Task<GriffinClaimsDto?> ValidateAndGetClaimsAsync(string token, string griffinBaseUrl, int timeoutSeconds)
    {
        // Compute cache key from token hash
        var cacheKey = $"griffin_claims_{ComputeSHA256Hash(token)}";

        // Try cache first
        if (_cache.TryGetValue<GriffinClaimsDto>(cacheKey, out var cachedClaims))
        {
            _logger.LogDebug("Griffin claims cache hit");
            return cachedClaims;
        }

        _logger.LogDebug("Griffin claims cache miss, calling API");

        // Validate token
        var isValid = await ValidateTokenAsync(token, griffinBaseUrl, timeoutSeconds);
        if (!isValid)
        {
            return null;
        }

        // Get claims
        var claims = await GetClaimsAsync(token, griffinBaseUrl, timeoutSeconds);
        if (claims == null)
        {
            return null;
        }

        // Cache with 2-hour TTL (reduced from 8h for security — limits stale credential window)
        _cache.Set(cacheKey, claims, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(2),
            Priority = CacheItemPriority.Normal
        });

        return claims;
    }

    public async Task<ClaimsPrincipal?> AuthenticateUserAsync(string token, GriffinConfig config, string ipAddress)
    {
        // Validate and get claims
        var griffinClaims = await ValidateAndGetClaimsAsync(token, config.BaseUrl!, config.TimeoutSeconds);
        if (griffinClaims == null)
        {
            _logger.LogWarning("Griffin authentication failed: invalid token or claims");
            _securityLogger.LogAuthenticationFailure("Griffin SSO", ipAddress, "Invalid token or claims retrieval failed");
            return null;
        }

        // Validate required claims
        if (string.IsNullOrWhiteSpace(griffinClaims.UPN) || string.IsNullOrWhiteSpace(griffinClaims.sAMAccountName))
        {
            _logger.LogError("Griffin claims missing required fields (UPN or sAMAccountName)");
            _securityLogger.LogSecurityThreat("MissingClaims", "Griffin token claims missing UPN or sAMAccountName — possible token tampering", ipAddress);
            return null;
        }

        // Lookup user by UPN (email)
        // SECURITY-AUDITED: SAFE — authentication must search across all companies to find user by email/UPN
        var user = await _dbContext.Users
            .IgnoreQueryFilters() // Search across all companies
            .Include(u => u.RoleTemplate)
            .Include(u => u.JobType)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == griffinClaims.UPN.ToLower() && u.IsActive);

        // Handle user provisioning
        if (user == null)
        {
            if (config.AutoProvisionUsers)
            {
                user = await AutoProvisionUserAsync(griffinClaims, config);
                if (user == null)
                {
                    _logger.LogError("Failed to auto-provision user for UPN: {UPN}", griffinClaims.UPN);
                    _securityLogger.LogAuthenticationFailure(griffinClaims.UPN, ipAddress, "Auto-provisioning failed");
                    return null;
                }
            }
            else
            {
                _logger.LogInformation("User {UPN} not found and auto-provisioning disabled", griffinClaims.UPN);
                _securityLogger.LogAuthenticationFailure(griffinClaims.UPN, ipAddress, "User not found and auto-provisioning disabled");
                return null; // Will show pending approval message in callback
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

        // v3.0 Organizational Hierarchy - fetch early so we can use for grant provisioning and claims
        var hierarchyContext = await _hierarchyService.GetUserHierarchyContextAsync(user.Id);

        // Login-time grant provisioning: if user has a RoleTemplate but no grants, provision from AutoGrants
        if (user.RoleTemplateId.HasValue)
        {
            var hasAnyGrants = await _dbContext.Grants.AnyAsync(g => g.UserId == user.Id);
            if (!hasAnyGrants)
            {
                var roleScope = new GrantScope(
                    ProjectId: hierarchyContext?.Path.Project?.Id,
                    AreaId: hierarchyContext?.Path.Area?.Id,
                    MoleculeId: hierarchyContext?.Path.Molecule?.Id,
                    CompanyId: user.CompanyId
                );
                await _grantService.ApplyAutoGrantsAsync(user.Id, user.RoleTemplateId.Value, roleScope);
                _logger.LogInformation("Provisioned auto-grants for Griffin user {UserId} from RoleTemplate {TemplateId}",
                    user.Id, user.RoleTemplateId.Value);
            }
        }

        // Build ClaimsPrincipal
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, griffinClaims.DisplayName),
            new Claim(ClaimTypes.Email, griffinClaims.UPN),
            new Claim(ClaimTypes.GivenName, griffinClaims.GivenName),
            new Claim(ClaimTypes.Surname, griffinClaims.Surname),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("CompanyId", user.CompanyId.ToString()),
            new Claim("AuthMethod", "Griffin"),
            new Claim("Griffin:sAMAccountName", griffinClaims.sAMAccountName),
            // HIGH-007 FIX: Store hashed token reference instead of raw token to prevent cookie theft
            new Claim("Griffin:TokenHash", ComputeSHA256Hash(token)),
            new Claim("Griffin:AuthTime", griffinClaims.auth_time),
            new Claim("AuthTimestamp", DateTime.UtcNow.ToString("o"))
        };

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

        var identity = new ClaimsIdentity(claims, "Griffin");
        var principal = new ClaimsPrincipal(identity);

        _logger.LogInformation("Griffin authentication successful for user {UserId} ({Email})", user.Id, user.Email);
        _securityLogger.LogAuthenticationSuccess(user.Id, user.Email, user.Role.ToString(), ipAddress);

        return principal;
    }

    private async Task<AppUser?> AutoProvisionUserAsync(GriffinClaimsDto griffinClaims, GriffinConfig config)
    {
        try
        {
            var user = new AppUser
            {
                CompanyId = config.CompanyId,
                Email = griffinClaims.UPN,
                DisplayName = griffinClaims.DisplayName,
                Role = config.DefaultProvisionedRole,
                RoleTemplateId = config.DefaultProvisionedRoleTemplateId,
                IsActive = true,
                PasswordHash = Array.Empty<byte>(), // No password for Griffin users
                PasswordSalt = Array.Empty<byte>()
            };

            // Sync Role from RoleTemplate if available
            if (config.DefaultProvisionedRoleTemplateId.HasValue)
            {
                var template = await _dbContext.RoleTemplates.FindAsync(config.DefaultProvisionedRoleTemplateId.Value);
                if (template?.DerivedUserRole.HasValue == true)
                {
                    user.Role = template.DerivedUserRole.Value;
                }
            }

            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Auto-provisioned Griffin user: {Email} with role {Role}, template {TemplateId}", user.Email, user.Role, user.RoleTemplateId);
            _securityLogger.LogSensitiveDataAccess(user.Id, "AppUser", "AutoProvision via Griffin SSO");
            _logger.LogWarning("Griffin auto-provisioned user {Email} has no molecule/hierarchy placement. " +
                "Owner or Manager must assign placement before user can access operational features (D-06).",
                user.Email);

            // Audit log
            await _auditLogService.LogSystemActionAsync(
                "GriffinAutoProvision",
                "AppUser",
                user.Id,
                $"Auto-provisioned Griffin user: {user.Email} — PENDING PLACEMENT (no molecule/hierarchy assigned)",
                $"Role={user.Role}, CompanyId={user.CompanyId}, UPN={griffinClaims.UPN}");

            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to auto-provision user for UPN: {UPN}", griffinClaims.UPN);
            _securityLogger.LogSecurityThreat("AutoProvisionError", $"Failed to auto-provision Griffin user {griffinClaims.UPN}: {ex.Message}", null);
            return null;
        }
    }

    /// <summary>
    /// Maps legacy UserRole + JobType to RoleTemplate key for login-time backfill.
    /// Delegates to centralized RoleTemplateMapper to ensure consistency across codebase.
    /// </summary>
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
