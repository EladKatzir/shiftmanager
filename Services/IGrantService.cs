using ShiftManager.Models;

namespace ShiftManager.Services;

public interface IGrantService
{
    // Grant checking
    Task<bool> HasGrantAsync(int userId, string grantKey);
    Task<bool> HasGrantAsync(int userId, string grantKey, GrantScope scope);
    Task<bool> HasGrantWithScopeAsync(int userId, string grantKey, int? projectId = null, int? areaId = null,
        int? moleculeId = null, int? departmentId = null, int? companyId = null, int? jobTypeId = null,
        int? targetUserId = null);

    /// <summary>
    /// Checks if the user has any calendar editing grant (shift, chore, or on-duty assignment).
    /// Used by text entry endpoints where multiple grant types authorize the same action.
    /// </summary>
    Task<bool> HasCalendarEditPermissionAsync(int userId);

    /// <summary>
    /// Gate for free-text QuickEntry creation/deletion on calendar cells. Broader than
    /// HasCalendarEditPermissionAsync: also accepts WriteOverviewNotes (grant 110) which every
    /// role has. Assignment actions still use HasCalendarEditPermissionAsync.
    /// </summary>
    Task<bool> HasCalendarNotePermissionAsync(int userId);

    /// <summary>
    /// Verifies note-writing access to the target user's company.
    /// Manager tier (any assign grant) → accessible-company set from assign grants (may be cross-company within molecule).
    /// Note-only tier (WriteOverviewNotes alone) → caller's own company only.
    /// Self-target (callerId == targetUserId) always allowed.
    /// </summary>
    Task<bool> CanReachUserForNoteAsync(int callerId, int targetUserId);

    // Grant queries
    Task<List<Grant>> GetUserGrantsAsync(int userId);
    Task<List<Grant>> GetUserGrantsByTypeAsync(int userId, int grantTypeId);
    Task<Grant?> GetGrantAsync(int grantId);
    Task<GrantType?> GetGrantTypeByKeyAsync(string key);
    Task<List<GrantType>> GetAllGrantTypesAsync();

    // Scope resolution - determines which entities a user can access based on their grants
    Task<List<int>> GetAccessibleCompanyIdsForGrantAsync(int userId, string grantKey);
    Task<List<int>> GetAccessibleMoleculeIdsForGrantAsync(int userId, string grantKey);
    Task<bool> HasGrantForCompanyAsync(int userId, string grantKey, int targetCompanyId);

    // Grant management
    Task<Grant?> GrantAsync(int userId, int grantTypeId, GrantScope scope, int? grantedByUserId = null, string? notes = null);
    Task<bool> RevokeAsync(int grantId, int? revokedByUserId = null);
    Task<bool> RevokeAllUserGrantsAsync(int userId, int? revokedByUserId = null);
    Task<bool> CanUserGrantAsync(int granterId, int grantTypeId, GrantScope targetScope);

    // Auto-grants from roles
    Task ApplyAutoGrantsAsync(int userId, int roleTemplateId, GrantScope roleScope);
    Task RemoveAutoGrantsAsync(int userId, int roleTemplateId);

    // Role template grant assignment for onboarding
    Task<int> AssignRoleTemplateGrantsAsync(int userId, string roleTemplateKey, GrantScope scope, int? grantedByUserId = null);
    /// <summary>Builds the GrantScope for a role template key against a target company's hierarchy.</summary>
    Task<GrantScope> BuildRoleTemplateScopeAsync(string roleTemplateKey, int companyId, int? jobTypeId);
    Task<GrantVerificationResult> VerifyUserGrantsAsync(int userId);
    Task<List<GrantVerificationResult>> VerifyAllUserGrantsAsync();
    Task<int> RepairUserGrantsAsync(int userId, int? repairedByUserId = null);
    Task<int> RepairAllUserGrantsAsync(int? repairedByUserId = null);
}

/// <summary>
/// Result of grant verification for a user.
/// </summary>
public class GrantVerificationResult
{
    public int UserId { get; set; }
    public string UserDisplayName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;
    public List<string> ExpectedGrants { get; set; } = new();
    public List<string> ActualGrants { get; set; } = new();
    public List<string> MissingGrants { get; set; } = new();
    public List<string> ExtraGrants { get; set; } = new();
    public bool IsCompliant => MissingGrants.Count == 0;
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
