using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

public class GrantService : IGrantService
{
    private readonly AppDbContext _db;
    private readonly IHierarchyService _hierarchyService;

    public GrantService(AppDbContext db, IHierarchyService hierarchyService)
    {
        _db = db;
        _hierarchyService = hierarchyService;
    }

    // Grant checking
    public async Task<bool> HasGrantAsync(int userId, string grantKey)
    {
        var grantType = await GetGrantTypeByKeyAsync(grantKey);
        if (grantType == null)
            return false;

        return await _db.Grants
            .AnyAsync(g => g.UserId == userId && g.GrantTypeId == grantType.Id && g.CanOwn);
    }

    public async Task<bool> HasGrantAsync(int userId, string grantKey, GrantScope scope)
    {
        return await HasGrantWithScopeAsync(userId, grantKey,
            scope.ProjectId, scope.AreaId, scope.MoleculeId,
            scope.DepartmentId, scope.CompanyId, scope.JobTypeId);
    }

    public async Task<bool> HasGrantWithScopeAsync(int userId, string grantKey, int? projectId = null, int? areaId = null,
        int? moleculeId = null, int? departmentId = null, int? companyId = null, int? jobTypeId = null)
    {
        var grantType = await GetGrantTypeByKeyAsync(grantKey);
        if (grantType == null)
            return false;

        // Get user's hierarchy context to check scope inheritance
        var userContext = await _hierarchyService.GetUserHierarchyContextAsync(userId);

        // Check for exact scope match or higher-level scope that includes this scope
        var grants = await _db.Grants
            .Where(g => g.UserId == userId && g.GrantTypeId == grantType.Id && g.CanOwn)
            .ToListAsync();

        foreach (var grant in grants)
        {
            // Self scope - user always has their own grants
            if (!grant.ProjectId.HasValue && !grant.AreaId.HasValue && !grant.MoleculeId.HasValue &&
                !grant.DepartmentId.HasValue && !grant.CompanyId.HasValue && !grant.JobTypeId.HasValue)
            {
                // Self-scoped grant - only valid for user's own resources
                continue;
            }

            // Project scope covers everything below
            if (grant.ProjectId.HasValue)
            {
                if (projectId.HasValue && grant.ProjectId == projectId)
                    return true;
                // Project scope also covers area, molecule, etc. if they belong to this project
                if (userContext != null && grant.ProjectId == userContext.Path.Project.Id)
                    return true;
            }

            // Area scope covers molecules, companies, departments below
            if (grant.AreaId.HasValue)
            {
                if (areaId.HasValue && grant.AreaId == areaId)
                    return true;
                if (userContext != null && grant.AreaId == userContext.Path.Area.Id)
                    return true;
            }

            // Molecule scope covers companies/departments below
            if (grant.MoleculeId.HasValue)
            {
                if (moleculeId.HasValue && grant.MoleculeId == moleculeId)
                    return true;
                if (userContext != null && grant.MoleculeId == userContext.Path.Molecule.Id)
                    return true;
            }

            // Department scope
            if (grant.DepartmentId.HasValue && departmentId.HasValue && grant.DepartmentId == departmentId)
                return true;

            // Company scope
            if (grant.CompanyId.HasValue && companyId.HasValue && grant.CompanyId == companyId)
                return true;

            // JobType scope (optionally combined with company)
            if (grant.JobTypeId.HasValue && jobTypeId.HasValue && grant.JobTypeId == jobTypeId)
            {
                if (!grant.CompanyId.HasValue || (companyId.HasValue && grant.CompanyId == companyId))
                    return true;
            }
        }

        return false;
    }

    // Grant queries
    public async Task<List<Grant>> GetUserGrantsAsync(int userId)
    {
        return await _db.Grants
            .Include(g => g.GrantType)
            .Where(g => g.UserId == userId)
            .OrderBy(g => g.GrantType.Category)
            .ThenBy(g => g.GrantType.Key)
            .ToListAsync();
    }

    public async Task<List<Grant>> GetUserGrantsByTypeAsync(int userId, int grantTypeId)
    {
        return await _db.Grants
            .Include(g => g.GrantType)
            .Where(g => g.UserId == userId && g.GrantTypeId == grantTypeId)
            .ToListAsync();
    }

    public async Task<Grant?> GetGrantAsync(int grantId)
    {
        return await _db.Grants
            .Include(g => g.GrantType)
            .Include(g => g.User)
            .FirstOrDefaultAsync(g => g.Id == grantId);
    }

    public async Task<GrantType?> GetGrantTypeByKeyAsync(string key)
    {
        return await _db.GrantTypes
            .FirstOrDefaultAsync(gt => gt.Key == key && gt.IsActive);
    }

    public async Task<List<GrantType>> GetAllGrantTypesAsync()
    {
        return await _db.GrantTypes
            .Where(gt => gt.IsActive)
            .OrderBy(gt => gt.Category)
            .ThenBy(gt => gt.Key)
            .ToListAsync();
    }

    // Grant management
    public async Task<Grant?> GrantAsync(int userId, int grantTypeId, GrantScope scope, int? grantedByUserId = null, string? notes = null)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null)
            return null;

        var grantType = await _db.GrantTypes.FindAsync(grantTypeId);
        if (grantType == null || !grantType.IsActive)
            return null;

        // Check if grant already exists with same scope
        var existingGrant = await _db.Grants
            .FirstOrDefaultAsync(g => g.UserId == userId && g.GrantTypeId == grantTypeId &&
                g.ProjectId == scope.ProjectId && g.AreaId == scope.AreaId &&
                g.MoleculeId == scope.MoleculeId && g.DepartmentId == scope.DepartmentId &&
                g.CompanyId == scope.CompanyId && g.JobTypeId == scope.JobTypeId);

        if (existingGrant != null)
        {
            // Update existing grant
            existingGrant.CanOwn = true;
            existingGrant.GrantedByUserId = grantedByUserId;
            existingGrant.GrantedAt = DateTime.UtcNow;
            existingGrant.Notes = notes;
            await _db.SaveChangesAsync();
            return existingGrant;
        }

        // Create new grant
        var grant = new Grant
        {
            UserId = userId,
            GrantTypeId = grantTypeId,
            ProjectId = scope.ProjectId,
            AreaId = scope.AreaId,
            MoleculeId = scope.MoleculeId,
            DepartmentId = scope.DepartmentId,
            CompanyId = scope.CompanyId,
            JobTypeId = scope.JobTypeId,
            CanOwn = true,
            CanGive = false,
            GrantedByUserId = grantedByUserId,
            GrantedAt = DateTime.UtcNow,
            Notes = notes,
            IsAutoGrant = false
        };

        _db.Grants.Add(grant);
        await _db.SaveChangesAsync();

        return grant;
    }

    public async Task<bool> RevokeAsync(int grantId, int? revokedByUserId = null)
    {
        var grant = await _db.Grants.FindAsync(grantId);
        if (grant == null)
            return false;

        _db.Grants.Remove(grant);
        await _db.SaveChangesAsync();

        return true;
    }

    public async Task<bool> RevokeAllUserGrantsAsync(int userId)
    {
        var grants = await _db.Grants.Where(g => g.UserId == userId).ToListAsync();
        _db.Grants.RemoveRange(grants);
        await _db.SaveChangesAsync();
        return true;
    }

    // Auto-grants from roles
    public async Task ApplyAutoGrantsAsync(int userId, int roleTemplateId, GrantScope roleScope)
    {
        var roleTemplate = await _db.RoleTemplates
            .Include(rt => rt.AutoGrants)
            .ThenInclude(ag => ag.GrantType)
            .FirstOrDefaultAsync(rt => rt.Id == roleTemplateId);

        if (roleTemplate == null)
            return;

        foreach (var autoGrant in roleTemplate.AutoGrants)
        {
            // Determine effective scope based on ScopeMode
            var effectiveScope = DetermineEffectiveScope(autoGrant.ScopeMode, roleScope);

            // Create the grant
            var grant = new Grant
            {
                UserId = userId,
                GrantTypeId = autoGrant.GrantTypeId,
                ProjectId = effectiveScope.ProjectId,
                AreaId = effectiveScope.AreaId,
                MoleculeId = effectiveScope.MoleculeId,
                DepartmentId = effectiveScope.DepartmentId,
                CompanyId = effectiveScope.CompanyId,
                JobTypeId = effectiveScope.JobTypeId,
                CanOwn = autoGrant.CanOwn,
                CanGive = autoGrant.CanGive,
                GrantedAt = DateTime.UtcNow,
                IsAutoGrant = true,
                Notes = $"Auto-granted from role: {roleTemplate.Key}"
            };

            // Check if already exists
            var existing = await _db.Grants.FirstOrDefaultAsync(g =>
                g.UserId == userId && g.GrantTypeId == autoGrant.GrantTypeId &&
                g.ProjectId == effectiveScope.ProjectId && g.AreaId == effectiveScope.AreaId &&
                g.MoleculeId == effectiveScope.MoleculeId && g.DepartmentId == effectiveScope.DepartmentId &&
                g.CompanyId == effectiveScope.CompanyId && g.JobTypeId == effectiveScope.JobTypeId);

            if (existing == null)
            {
                _db.Grants.Add(grant);
            }
        }

        await _db.SaveChangesAsync();
    }

    public async Task RemoveAutoGrantsAsync(int userId, int roleTemplateId)
    {
        var roleTemplate = await _db.RoleTemplates
            .Include(rt => rt.AutoGrants)
            .FirstOrDefaultAsync(rt => rt.Id == roleTemplateId);

        if (roleTemplate == null)
            return;

        var grantTypeIds = roleTemplate.AutoGrants.Select(ag => ag.GrantTypeId).ToList();

        var autoGrants = await _db.Grants
            .Where(g => g.UserId == userId && g.IsAutoGrant && grantTypeIds.Contains(g.GrantTypeId))
            .ToListAsync();

        _db.Grants.RemoveRange(autoGrants);
        await _db.SaveChangesAsync();
    }

    private GrantScope DetermineEffectiveScope(GrantScopeMode scopeMode, GrantScope roleScope)
    {
        return scopeMode switch
        {
            GrantScopeMode.SameAsRole => roleScope,
            GrantScopeMode.ExpandToMolecule => new GrantScope(MoleculeId: roleScope.MoleculeId),
            GrantScopeMode.ExpandToArea => new GrantScope(AreaId: roleScope.AreaId),
            GrantScopeMode.Custom => roleScope,
            _ => roleScope
        };
    }
}
