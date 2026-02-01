using ShiftManager.Models;

namespace ShiftManager.Services;

public interface IGrantService
{
    // Grant checking
    Task<bool> HasGrantAsync(int userId, string grantKey);
    Task<bool> HasGrantAsync(int userId, string grantKey, GrantScope scope);
    Task<bool> HasGrantWithScopeAsync(int userId, string grantKey, int? projectId = null, int? areaId = null,
        int? moleculeId = null, int? departmentId = null, int? companyId = null, int? jobTypeId = null);

    // Grant queries
    Task<List<Grant>> GetUserGrantsAsync(int userId);
    Task<List<Grant>> GetUserGrantsByTypeAsync(int userId, int grantTypeId);
    Task<Grant?> GetGrantAsync(int grantId);
    Task<GrantType?> GetGrantTypeByKeyAsync(string key);
    Task<List<GrantType>> GetAllGrantTypesAsync();

    // Grant management
    Task<Grant?> GrantAsync(int userId, int grantTypeId, GrantScope scope, int? grantedByUserId = null, string? notes = null);
    Task<bool> RevokeAsync(int grantId, int? revokedByUserId = null);
    Task<bool> RevokeAllUserGrantsAsync(int userId);
    Task<bool> CanUserGrantAsync(int granterId, int grantTypeId, GrantScope targetScope);

    // Auto-grants from roles
    Task ApplyAutoGrantsAsync(int userId, int roleTemplateId, GrantScope roleScope);
    Task RemoveAutoGrantsAsync(int userId, int roleTemplateId);
}

/// <summary>
/// Scope for a grant - defines at what level the grant applies.
/// </summary>
public record GrantScope(
    int? ProjectId = null,
    int? AreaId = null,
    int? MoleculeId = null,
    int? DepartmentId = null,
    int? CompanyId = null,
    int? JobTypeId = null
)
{
    public static GrantScope Self() => new();
    public static GrantScope Company(int companyId) => new(CompanyId: companyId);
    public static GrantScope Department(int departmentId) => new(DepartmentId: departmentId);
    public static GrantScope Molecule(int moleculeId) => new(MoleculeId: moleculeId);
    public static GrantScope Area(int areaId) => new(AreaId: areaId);
    public static GrantScope Project(int projectId) => new(ProjectId: projectId);
    public static GrantScope JobType(int jobTypeId, int? companyId = null) => new(JobTypeId: jobTypeId, CompanyId: companyId);
}
