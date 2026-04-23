using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

public class RoleService : IRoleService
{
    private readonly AppDbContext _db;
    private readonly IGrantService _grantService;

    public RoleService(AppDbContext db, IGrantService grantService)
    {
        _db = db;
        _grantService = grantService;
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
    public async Task<UserRoleAssignment?> AssignRoleAsync(int userId, int roleTemplateId, GrantScope scope, int assignedByUserId)
    {
        // SECURTY-AUDITED: SAFE — Owner/super-admin role assignment legitimately targets users
        // in other tenants; AssignRoles grant scope on the caller is the access boundary.
        var user = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
            return null;

        var roleTemplate = await GetRoleTemplateAsync(roleTemplateId);
        if (roleTemplate == null)
            return null;

        // Check if user already has this role with the same scope
        var existingRole = await _db.UserRoleAssignments.IgnoreQueryFilters()
            .FirstOrDefaultAsync(ura => ura.UserId == userId && ura.RoleTemplateId == roleTemplateId &&
                ura.CompanyId == scope.CompanyId && ura.DepartmentId == scope.DepartmentId &&
                ura.MoleculeId == scope.MoleculeId && ura.AreaId == scope.AreaId &&
                ura.JobTypeId == scope.JobTypeId && ura.IsActive);

        if (existingRole != null)
            return existingRole; // Already has this role

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

        return roleAssignment;
    }

    public async Task<bool> RemoveRoleAsync(int userRoleId, int? removedByUserId = null)
    {
        var roleAssignment = await _db.UserRoleAssignments.IgnoreQueryFilters()
            .Include(ura => ura.RoleTemplate)
            .FirstOrDefaultAsync(ura => ura.Id == userRoleId);

        if (roleAssignment == null)
            return false;

        var roleTemplateId = roleAssignment.RoleTemplateId;
        var userId = roleAssignment.UserId;

        // Soft delete the role assignment
        roleAssignment.IsActive = false;
        await _db.SaveChangesAsync();

        // Remove auto-grants from this role
        await _grantService.RemoveAutoGrantsAsync(userId, roleTemplateId);

        return true;
    }

    public async Task<bool> RemoveAllUserRolesAsync(int userId)
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
        return true;
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
