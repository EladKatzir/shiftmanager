using System.Security.Claims;
using Microsoft.Extensions.Logging;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

/// <summary>
/// Resolves tenant from user's CompanyId claim
/// Phase 2: Single-tenant behavior maintained via user claims
/// </summary>
public class TenantResolver : ITenantResolver
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<TenantResolver>? _logger;
    private int? _tenantIdOverride;

    public TenantResolver(IHttpContextAccessor httpContextAccessor, ILogger<TenantResolver>? logger = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public int GetCurrentTenantId()
    {
        // Priority 1: Explicit override (existing)
        if (_tenantIdOverride.HasValue)
        {
            _logger?.LogInformation("TenantResolver: Using override CompanyId={CompanyId}", _tenantIdOverride.Value);
            return _tenantIdOverride.Value;
        }

        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            // Priority 2: Owner's selected company (NEW)
            if (user.IsInRole(nameof(UserRole.Owner)))
            {
                var ownerService = _httpContextAccessor.HttpContext?.RequestServices
                    .GetService<IOwnerCompanySelectorService>();

                var selectedCompanyId = ownerService?.GetSelectedCompanyId();
                if (selectedCompanyId.HasValue)
                {
                    _logger?.LogInformation("TenantResolver: Owner using selected CompanyId={CompanyId}",
                        selectedCompanyId.Value);
                    return selectedCompanyId.Value;
                }

                _logger?.LogInformation("TenantResolver: Owner has no selection, using home CompanyId");
            }

            // Priority 2.5: Multi-company member's selected active company.
            // Validate the cookie against the MemberCompanyIds claim (baked at login) so this
            // stays synchronous — never trust the cookie alone.
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext?.Request.Cookies.TryGetValue(MemberSelectedCookieName, out var memberCookie) == true
                && int.TryParse(memberCookie, out var memberCompanyId))
            {
                var allowed = user.FindFirst("MemberCompanyIds")?.Value;
                if (!string.IsNullOrEmpty(allowed)
                    && allowed.Split(',').Contains(memberCompanyId.ToString()))
                {
                    _logger?.LogDebug("TenantResolver: member-selected CompanyId={CompanyId}", memberCompanyId);
                    return memberCompanyId;
                }
            }

            // Priority 3: User's CompanyId claim (default for all users)
            var email = user.FindFirst(ClaimTypes.Name)?.Value;
            var companyIdClaim = user.FindFirst("CompanyId");
            if (companyIdClaim != null && int.TryParse(companyIdClaim.Value, out var companyId))
            {
                // E-01: Log debug-level only (PII redaction); use userId for tracking instead of email
                _logger?.LogDebug("TenantResolver: User CompanyId claim={CompanyId}", companyId);
                return companyId;
            }
            else
            {
                // E-01: Don't log email — only log claim state for debugging
                _logger?.LogWarning("TenantResolver: Authenticated user has missing or invalid CompanyId claim. Claim value: {ClaimValue}",
                    companyIdClaim?.Value ?? "null");
            }
        }
        else
        {
            _logger?.LogWarning("TenantResolver: User not authenticated, no tenant access (CompanyId=0)");
        }

        // SECURITY FIX: Removed dangerous fallback to CompanyId=1
        // Unauthenticated requests should not have access to tenant data
        // Return 0 to indicate no tenant context (query filters will exclude all records)
        return 0;
    }

    public void SetCurrentTenantId(int companyId)
    {
        _tenantIdOverride = companyId;
    }

    public bool HasTenant()
    {
        return _tenantIdOverride.HasValue ||
               _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated == true;
    }

    private const string MemberSelectedCookieName = "member_selected_company";
    private const string DirectorMoleculeCookieName = "director_selected_molecule";

    public int? GetDirectorSelectedMoleculeId()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var user = httpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            return null;

        // Only relevant for Director/AreaAdmin roles
        if (!user.IsInRole(nameof(UserRole.Director)) && !user.IsInRole(nameof(UserRole.AreaAdmin)))
            return null;

        // Priority 1: Cookie (explicitly selected molecule)
        if (httpContext?.Request.Cookies.TryGetValue(DirectorMoleculeCookieName, out var cookieValue) == true
            && int.TryParse(cookieValue, out var moleculeId))
        {
            return moleculeId;
        }

        // Priority 2: MoleculeId claim (set at login)
        var moleculeIdClaim = user.FindFirst("MoleculeId");
        if (moleculeIdClaim != null && int.TryParse(moleculeIdClaim.Value, out var claimMoleculeId))
        {
            return claimMoleculeId;
        }

        return null;
    }
}