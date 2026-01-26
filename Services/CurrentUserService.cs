using System.Security.Claims;

namespace ShiftManager.Services;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    // Basic identity
    public int UserId => GetClaimValueAsInt(ClaimTypes.NameIdentifier) ?? 0;
    public string DisplayName => User?.Identity?.Name ?? string.Empty;
    public string Role => GetClaimValue(ClaimTypes.Role) ?? string.Empty;
    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    // Company/tenant
    public int? CompanyId => GetClaimValueAsInt("CompanyId");

    // v3.0 Organizational Hierarchy
    public int? MoleculeId => GetClaimValueAsInt("MoleculeId");
    public int? AreaId => GetClaimValueAsInt("AreaId");
    public int? ProjectId => GetClaimValueAsInt("ProjectId");
    public int? JobTypeId => GetClaimValueAsInt("JobTypeId");
    public int? DepartmentId => GetClaimValueAsInt("DepartmentId");

    // User type flags
    public bool IsWorkforce => GetClaimValueAsBool("IsWorkforce");
    public bool IsTech => GetClaimValueAsBool("IsTech");

    // Helper methods
    public bool HasClaim(string claimType)
    {
        return User?.HasClaim(c => c.Type == claimType) ?? false;
    }

    public string? GetClaimValue(string claimType)
    {
        return User?.FindFirstValue(claimType);
    }

    private int? GetClaimValueAsInt(string claimType)
    {
        var value = GetClaimValue(claimType);
        if (string.IsNullOrEmpty(value))
            return null;

        return int.TryParse(value, out var result) ? result : null;
    }

    private bool GetClaimValueAsBool(string claimType)
    {
        var value = GetClaimValue(claimType);
        if (string.IsNullOrEmpty(value))
            return false;

        return bool.TryParse(value, out var result) && result;
    }
}
