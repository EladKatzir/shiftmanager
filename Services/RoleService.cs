using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Results;
using ShiftManager.Models.Support;
using ShiftManager.Resources;

namespace ShiftManager.Services;

public class RoleService : IRoleService
{
    private readonly AppDbContext _db;
    private readonly IGrantService _grantService;
    private readonly ILogger<RoleService> _logger;
    private readonly IStringLocalizer<SharedResources> _localizer;

    public RoleService(
        AppDbContext db,
        IGrantService grantService,
        ILogger<RoleService> logger,
        IStringLocalizer<SharedResources> localizer)
    {
        _db = db;
        _grantService = grantService;
        _logger = logger;
        _localizer = localizer;
    }

    // Role template queries
    public async Task<RoleTemplate?> GetRoleTemplateAsync(int roleTemplateId)
    {
        return await _db.RoleTemplates.IgnoreQueryFilters()
            .Include(rt => rt.AutoGrants)
            .ThenInclude(ag => ag.GrantType)
            .FirstOrDefaultAsync(rt => rt.Id == roleTemplateId && rt.IsActive);
    }

    public async Task<RoleTemplate?> GetRoleTemplateByKeyAsync(string key)
    {
        return await _db.RoleTemplates.IgnoreQueryFilters()
            .Include(rt => rt.AutoGrants)
            .ThenInclude(ag => ag.GrantType)
            .FirstOrDefaultAsync(rt => rt.Key == key && rt.IsActive);
    }

    public async Task<List<RoleTemplate>> GetRoleTemplatesAsync()
    {
        return await _db.RoleTemplates.IgnoreQueryFilters()
            .Include(rt => rt.AutoGrants)
            .Where(rt => rt.IsActive)
            .OrderBy(rt => rt.SortOrder)
            .ThenBy(rt => rt.Key)
            .ToListAsync();
    }

    public async Task<List<RoleTemplate>> GetRoleTemplatesByScopeLevelAsync(RoleScopeLevel scopeLevel)
    {
        return await _db.RoleTemplates.IgnoreQueryFilters()
            .Include(rt => rt.AutoGrants)
            .Where(rt => rt.IsActive && rt.ScopeLevel == scopeLevel)
            .OrderBy(rt => rt.SortOrder)
            .ToListAsync();
    }

    // User role queries
    public async Task<List<UserRoleAssignment>> GetUserRolesAsync(int userId)
    {
        return await _db.UserRoleAssignments.IgnoreQueryFilters()
            .Include(ura => ura.RoleTemplate)
            .Include(ura => ura.Company)
            .Include(ura => ura.Department)
            .Include(ura => ura.Molecule)
            .Include(ura => ura.Area)
            .Include(ura => ura.JobType)
            .Where(ura => ura.UserId == userId && ura.IsActive)
            .OrderBy(ura => ura.RoleTemplate.SortOrder)
            .ToListAsync();
    }

    public async Task<List<UserRoleAssignment>> GetUserRolesInScopeAsync(int userId, GrantScope scope)
    {
        var query = _db.UserRoleAssignments.IgnoreQueryFilters()
            .Include(ura => ura.RoleTemplate)
            .Where(ura => ura.UserId == userId && ura.IsActive);

        if (scope.CompanyId.HasValue)
            query = query.Where(ura => ura.CompanyId == scope.CompanyId);
        if (scope.DepartmentId.HasValue)
            query = query.Where(ura => ura.DepartmentId == scope.DepartmentId);
        if (scope.MoleculeId.HasValue)
            query = query.Where(ura => ura.MoleculeId == scope.MoleculeId);
        if (scope.AreaId.HasValue)
            query = query.Where(ura => ura.AreaId == scope.AreaId);
        if (scope.JobTypeId.HasValue)
            query = query.Where(ura => ura.JobTypeId == scope.JobTypeId);

        return await query.ToListAsync();
    }

    public async Task<UserRoleAssignment?> GetUserRoleAssignmentAsync(int userRoleId)
    {
        return await _db.UserRoleAssignments.IgnoreQueryFilters()
            .Include(ura => ura.RoleTemplate)
            .Include(ura => ura.User)
            .FirstOrDefaultAsync(ura => ura.Id == userRoleId);
    }

    public async Task<bool> UserHasRoleAsync(int userId, string roleKey)
    {
        return await _db.UserRoleAssignments.IgnoreQueryFilters()
            .Include(ura => ura.RoleTemplate)
            .AnyAsync(ura => ura.UserId == userId && ura.IsActive && ura.RoleTemplate.Key == roleKey);
    }

    public async Task<bool> UserHasRoleAsync(int userId, int roleTemplateId)
    {
        return await _db.UserRoleAssignments.IgnoreQueryFilters()
            .AnyAsync(ura => ura.UserId == userId && ura.IsActive && ura.RoleTemplateId == roleTemplateId);
    }

    // Role assignment management
    public async Task<OperationResult<UserRoleAssignment>> AssignRoleAsync(int userId, int roleTemplateId, GrantScope scope, int assignedByUserId)
    {
        // SECURITY-AUDITED: SAFE — Owner/super-admin role assignment legitimately targets users
        // in other tenants. The access boundary is enforced UPSTREAM by the caller:
        //   Pages/Admin/Organization/Roles/Assign.cshtml.cs gates with the AssignRoles grant.
        // IRoleService is internal-only; any new caller MUST enforce an equivalent grant check
        // before invoking this method, otherwise a cross-tenant IDOR is introduced here.
        var user = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            _logger.LogWarning("AssignRoleAsync: user {UserId} not found", userId);
            return OperationResult<UserRoleAssignment>.Fail(
                "Error_RoleService_UserNotFound",
                _localizer["Error_RoleService_UserNotFound"].Value);
        }

        var roleTemplate = await GetRoleTemplateAsync(roleTemplateId);
        if (roleTemplate == null)
        {
            _logger.LogWarning("AssignRoleAsync: roleTemplate {RoleTemplateId} not found or inactive", roleTemplateId);
            return OperationResult<UserRoleAssignment>.Fail(
                "Error_RoleService_TemplateNotFound",
                _localizer["Error_RoleService_TemplateNotFound"].Value);
        }

        // Check if user already has this role with the same scope
        var existingRole = await _db.UserRoleAssignments.IgnoreQueryFilters()
            .FirstOrDefaultAsync(ura => ura.UserId == userId && ura.RoleTemplateId == roleTemplateId &&
                ura.CompanyId == scope.CompanyId && ura.DepartmentId == scope.DepartmentId &&
                ura.MoleculeId == scope.MoleculeId && ura.AreaId == scope.AreaId &&
                ura.JobTypeId == scope.JobTypeId && ura.IsActive);

        if (existingRole != null)
            return OperationResult<UserRoleAssignment>.Ok(existingRole); // Already has this role

        // Create role assignment
        var roleAssignment = new UserRoleAssignment
        {
            UserId = userId,
            RoleTemplateId = roleTemplateId,
            CompanyId = scope.CompanyId,
            DepartmentId = scope.DepartmentId,
            MoleculeId = scope.MoleculeId,
            AreaId = scope.AreaId,
            JobTypeId = scope.JobTypeId,
            AssignedByUserId = assignedByUserId,
            AssignedAt = DateTime.UtcNow,
            IsActive = true
        };

        _db.UserRoleAssignments.Add(roleAssignment);
        await _db.SaveChangesAsync();

        // Apply auto-grants from the role template
        await _grantService.ApplyAutoGrantsAsync(userId, roleTemplateId, scope);

        _logger.LogInformation(
            "Assigned role {RoleTemplateId} to user {UserId} (assignmentId {AssignmentId}) by {AssignedBy}",
            roleTemplateId, userId, roleAssignment.Id, assignedByUserId);
        return OperationResult<UserRoleAssignment>.Ok(roleAssignment);
    }

    public async Task<OperationResult> RemoveRoleAsync(int userRoleId, int? removedByUserId = null)
    {
        var roleAssignment = await _db.UserRoleAssignments.IgnoreQueryFilters()
            .Include(ura => ura.RoleTemplate)
            .FirstOrDefaultAsync(ura => ura.Id == userRoleId);

        if (roleAssignment == null)
        {
            _logger.LogWarning("RemoveRoleAsync: assignment {UserRoleId} not found", userRoleId);
            return OperationResult.Fail(
                "Error_RoleService_AssignmentNotFound",
                _localizer["Error_RoleService_AssignmentNotFound"].Value);
        }

        var roleTemplateId = roleAssignment.RoleTemplateId;
        var userId = roleAssignment.UserId;

        // Soft delete the role assignment
        roleAssignment.IsActive = false;
        await _db.SaveChangesAsync();

        // Remove auto-grants from this role
        await _grantService.RemoveAutoGrantsAsync(userId, roleTemplateId);

        _logger.LogInformation(
            "Removed role assignment {UserRoleId} (user {UserId} role {RoleTemplateId}) by {RemovedBy}",
            userRoleId, userId, roleTemplateId, removedByUserId);
        return OperationResult.Ok();
    }

    public async Task<OperationResult> RemoveAllUserRolesAsync(int userId)
    {
        var roles = await _db.UserRoleAssignments.IgnoreQueryFilters()
            .Where(ura => ura.UserId == userId && ura.IsActive)
            .ToListAsync();

        foreach (var role in roles)
        {
            role.IsActive = false;
            await _grantService.RemoveAutoGrantsAsync(userId, role.RoleTemplateId);
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Removed all {Count} active roles for user {UserId}", roles.Count, userId);
        return OperationResult.Ok();
    }

    // Queries for role holders
    public async Task<List<AppUser>> GetUsersWithRoleAsync(int roleTemplateId)
    {
        return await _db.UserRoleAssignments.IgnoreQueryFilters()
            .Include(ura => ura.User)
            .Where(ura => ura.RoleTemplateId == roleTemplateId && ura.IsActive && ura.User.IsActive)
            .Select(ura => ura.User)
            .Distinct()
            .OrderBy(u => u.DisplayName)
            .ToListAsync();
    }

    public async Task<List<AppUser>> GetUsersWithRoleInScopeAsync(int roleTemplateId, GrantScope scope)
    {
        var query = _db.UserRoleAssignments.IgnoreQueryFilters()
            .Include(ura => ura.User)
            .Where(ura => ura.RoleTemplateId == roleTemplateId && ura.IsActive && ura.User.IsActive);

        if (scope.CompanyId.HasValue)
            query = query.Where(ura => ura.CompanyId == scope.CompanyId);
        if (scope.DepartmentId.HasValue)
            query = query.Where(ura => ura.DepartmentId == scope.DepartmentId);
        if (scope.MoleculeId.HasValue)
            query = query.Where(ura => ura.MoleculeId == scope.MoleculeId);
        if (scope.AreaId.HasValue)
            query = query.Where(ura => ura.AreaId == scope.AreaId);
        if (scope.JobTypeId.HasValue)
            query = query.Where(ura => ura.JobTypeId == scope.JobTypeId);

        return await query
            .Select(ura => ura.User)
            .Distinct()
            .OrderBy(u => u.DisplayName)
            .ToListAsync();
    }

    // Filtered template queries
    public async Task<List<RoleTemplate>> GetAssignableRoleTemplatesAsync()
    {
        return await _db.RoleTemplates.IgnoreQueryFilters()
            .Where(rt => rt.IsActive && rt.CanBeAssignedByDefault)
            .OrderBy(rt => rt.SortOrder)
            .ToListAsync();
    }

    public async Task<List<RoleTemplate>> GetSignupRoleTemplatesAsync()
    {
        return await _db.RoleTemplates.IgnoreQueryFilters()
            .Where(rt => rt.IsActive && rt.IsVisibleInSignup)
            .OrderBy(rt => rt.SortOrder)
            .ToListAsync();
    }

    public async Task<RoleTemplate?> GetRoleTemplateWithAutoGrantsAsync(int roleTemplateId)
    {
        return await _db.RoleTemplates.IgnoreQueryFilters()
            .Include(rt => rt.AutoGrants)
                .ThenInclude(ag => ag.GrantType)
            .FirstOrDefaultAsync(rt => rt.Id == roleTemplateId);
    }

    public async Task<RoleTemplate?> GetRoleTemplateWithAutoGrantsByKeyAsync(string key)
    {
        return await _db.RoleTemplates.IgnoreQueryFilters()
            .Include(rt => rt.AutoGrants)
                .ThenInclude(ag => ag.GrantType)
            .FirstOrDefaultAsync(rt => rt.Key == key && rt.IsActive);
    }

    // Bulk template queries for admin pages
    public async Task<List<RoleTemplate>> GetActiveRoleTemplatesWithAutoGrantsAsync()
    {
        return await _db.RoleTemplates.IgnoreQueryFilters()
            .Include(rt => rt.AutoGrants)
                .ThenInclude(ag => ag.GrantType)
            .Where(rt => rt.IsActive)
            .OrderBy(rt => rt.SortOrder)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<List<RoleTemplate>> GetAllRoleTemplatesWithDetailsAsync()
    {
        return await _db.RoleTemplates.IgnoreQueryFilters()
            .Include(rt => rt.AutoGrants)
            .Include(rt => rt.UserRoles)
            .OrderBy(rt => rt.SortOrder)
            .ThenBy(rt => rt.Key)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<int> GetActiveRoleTemplateCountAsync()
    {
        return await _db.RoleTemplates.IgnoreQueryFilters()
            .CountAsync(rt => rt.IsActive);
    }

    public async Task<int> GetRoleTemplateCountAsync()
    {
        return await _db.RoleTemplates.IgnoreQueryFilters()
            .CountAsync();
    }
}
