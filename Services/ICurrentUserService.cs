namespace ShiftManager.Services;

public interface ICurrentUserService
{
    // Basic identity
    int UserId { get; }
    string DisplayName { get; }
    string Role { get; }
    bool IsAuthenticated { get; }

    // Company/tenant
    int? CompanyId { get; }

    // v3.0 Organizational Hierarchy
    int? MoleculeId { get; }
    int? AreaId { get; }
    int? ProjectId { get; }
    int? JobTypeId { get; }
    int? DepartmentId { get; }

    // User type flags
    bool IsWorkforce { get; }
    bool IsTech { get; }

    // Helper methods
    bool HasClaim(string claimType);
    string? GetClaimValue(string claimType);
}
