namespace ShiftManager.Services;

/// <summary>
/// Service for checking Director permissions.
/// All methods are async because grant resolution is database-backed and
/// must not be called from a synchronous context — see Batch D in
/// .claude-reviews/2026-04-28/00-index.md (closes F-H-003, F-A-002, F-R-001).
/// </summary>
public interface IDirectorService
{
    /// <summary>
    /// Check if the current user is a Director
    /// </summary>
    Task<bool> IsDirectorAsync();

    /// <summary>
    /// Check if the current user is a Director of a specific company
    /// </summary>
    Task<bool> IsDirectorOfAsync(int companyId);

    /// <summary>
    /// Get all company IDs that the current user is a Director of
    /// </summary>
    Task<List<int>> GetDirectorCompanyIdsAsync();

    /// <summary>
    /// Get all company IDs that a specific user is a Director of
    /// </summary>
    Task<List<int>> GetDirectorCompanyIdsAsync(int userId);

    /// <summary>
    /// Check if current user can manage a specific company (either as Director, Manager, or Owner of that company)
    /// </summary>
    Task<bool> CanManageCompanyAsync(int companyId);

    /// <summary>
    /// Check if current user can assign the specified role
    /// Directors cannot assign Owner role
    /// </summary>
    Task<bool> CanAssignRoleAsync(string role);

    /// <summary>
    /// Check if current user can assign the specified role (strongly-typed)
    /// Owner: can assign any role
    /// Director: can assign Employee, Manager, Director only
    /// Manager: can assign Employee only
    /// Employee: cannot assign any role
    /// </summary>
    Task<bool> CanAssignRoleAsync(Models.Support.UserRole targetRole);
}
