using ShiftManager.Models;
using ShiftManager.Models.Results;

namespace ShiftManager.Services;

public interface IRoleService
{
    // Role template queries
    Task<RoleTemplate?> GetRoleTemplateAsync(int roleTemplateId);
    Task<RoleTemplate?> GetRoleTemplateByKeyAsync(string key);
    Task<List<RoleTemplate>> GetRoleTemplatesAsync();
    Task<List<RoleTemplate>> GetRoleTemplatesByScopeLevelAsync(Models.Support.RoleScopeLevel scopeLevel);

    // User role queries
    Task<List<UserRoleAssignment>> GetUserRolesAsync(int userId);
    Task<List<UserRoleAssignment>> GetUserRolesInScopeAsync(int userId, GrantScope scope);
    Task<UserRoleAssignment?> GetUserRoleAssignmentAsync(int userRoleId);
    Task<bool> UserHasRoleAsync(int userId, string roleKey);
    Task<bool> UserHasRoleAsync(int userId, int roleTemplateId);

    // Role assignment management — migrated to OperationResult to eliminate silent
    // null/false failures that the audit flagged. ErrorKeys: Error_RoleService_*.
    Task<OperationResult<UserRoleAssignment>> AssignRoleAsync(int userId, int roleTemplateId, GrantScope scope, int assignedByUserId);
    Task<OperationResult> RemoveRoleAsync(int userRoleId, int? removedByUserId = null);
    Task<OperationResult> RemoveAllUserRolesAsync(int userId);

    // Queries for role holders
    Task<List<AppUser>> GetUsersWithRoleAsync(int roleTemplateId);
    Task<List<AppUser>> GetUsersWithRoleInScopeAsync(int roleTemplateId, GrantScope scope);

    // Filtered template queries
    Task<List<RoleTemplate>> GetAssignableRoleTemplatesAsync();
    Task<List<RoleTemplate>> GetSignupRoleTemplatesAsync();

    // Templates with AutoGrants navigation (for grant management admin pages)
    Task<RoleTemplate?> GetRoleTemplateWithAutoGrantsAsync(int roleTemplateId);
    Task<RoleTemplate?> GetRoleTemplateWithAutoGrantsByKeyAsync(string key);

    // Bulk template queries for admin pages
    Task<List<RoleTemplate>> GetActiveRoleTemplatesWithAutoGrantsAsync();
    Task<List<RoleTemplate>> GetAllRoleTemplatesWithDetailsAsync();

    // Count for stats pages
    Task<int> GetActiveRoleTemplateCountAsync();
    Task<int> GetRoleTemplateCountAsync();
}
