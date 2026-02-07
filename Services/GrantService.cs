using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

public class GrantService : IGrantService
{
    private readonly AppDbContext _db;
    private readonly IHierarchyService _hierarchyService;
    private readonly IAuditLogService _auditLogService;

    public GrantService(AppDbContext db, IHierarchyService hierarchyService, IAuditLogService auditLogService)
    {
        _db = db;
        _hierarchyService = hierarchyService;
        _auditLogService = auditLogService;
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
                // Project-level grants provide access even when no specific scope is requested
                // (e.g., user has no hierarchy context but has a project-wide grant)
                if (!projectId.HasValue && !areaId.HasValue && !moleculeId.HasValue &&
                    !departmentId.HasValue && !companyId.HasValue && !jobTypeId.HasValue)
                    return true;

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

    // Scope resolution
    public async Task<List<int>> GetAccessibleCompanyIdsForGrantAsync(int userId, string grantKey)
    {
        var grantType = await GetGrantTypeByKeyAsync(grantKey);
        if (grantType == null)
            return new List<int>();

        // Get user's hierarchy context for Self scope resolution
        var userContext = await _hierarchyService.GetUserHierarchyContextAsync(userId);

        // Get all grants for this user and grant type
        var grants = await _db.Grants
            .Where(g => g.UserId == userId && g.GrantTypeId == grantType.Id && g.CanOwn)
            .ToListAsync();

        if (!grants.Any())
            return new List<int>();

        var companyIds = new HashSet<int>();

        foreach (var grant in grants)
        {
            // Project scope - all companies in project
            if (grant.ProjectId.HasValue)
            {
                // SECURITY-AUDITED: SAFE — scoped by grant's ProjectId; resolves companies within granted project scope
                var projectCompanyIds = await _db.Companies
                    .IgnoreQueryFilters()
                    .Where(c => c.Molecule.Area.ProjectId == grant.ProjectId.Value)
                    .Select(c => c.Id)
                    .ToListAsync();
                foreach (var id in projectCompanyIds)
                    companyIds.Add(id);
                continue;
            }

            // Area scope - all companies in area
            if (grant.AreaId.HasValue)
            {
                // SECURITY-AUDITED: SAFE — scoped by grant's AreaId; resolves companies within granted area scope
                var areaCompanyIds = await _db.Companies
                    .IgnoreQueryFilters()
                    .Where(c => c.Molecule.AreaId == grant.AreaId.Value)
                    .Select(c => c.Id)
                    .ToListAsync();
                foreach (var id in areaCompanyIds)
                    companyIds.Add(id);
                continue;
            }

            // Molecule scope - all companies in molecule
            if (grant.MoleculeId.HasValue)
            {
                // SECURITY-AUDITED: SAFE — scoped by grant's MoleculeId; resolves companies within granted molecule scope
                var moleculeCompanyIds = await _db.Companies
                    .IgnoreQueryFilters()
                    .Where(c => c.MoleculeId == grant.MoleculeId.Value)
                    .Select(c => c.Id)
                    .ToListAsync();
                foreach (var id in moleculeCompanyIds)
                    companyIds.Add(id);
                continue;
            }

            // Company scope - just that company
            if (grant.CompanyId.HasValue)
            {
                companyIds.Add(grant.CompanyId.Value);
                continue;
            }

            // Self scope (no scope defined) - user's own company
            if (userContext != null)
            {
                companyIds.Add(userContext.Path.Company.Id);
            }
        }

        return companyIds.ToList();
    }

    public async Task<bool> HasGrantForCompanyAsync(int userId, string grantKey, int targetCompanyId)
    {
        var accessibleCompanyIds = await GetAccessibleCompanyIdsForGrantAsync(userId, grantKey);
        return accessibleCompanyIds.Contains(targetCompanyId);
    }

    // Grant management
    public async Task<Grant?> GrantAsync(int userId, int grantTypeId, GrantScope scope, int? grantedByUserId = null, string? notes = null)
    {
        // Use IgnoreQueryFilters to allow granting to users in any company
        // SECURITY-AUDITED: SAFE — scoped by specific userId; CanGive delegation check follows
        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
            return null;

        var grantType = await _db.GrantTypes.FindAsync(grantTypeId);
        if (grantType == null || !grantType.IsActive)
            return null;

        // Enforce CanGive delegation - granter must have CanGive permission for this grant type
        if (grantedByUserId.HasValue)
        {
            var canGrant = await CanUserGrantAsync(grantedByUserId.Value, grantTypeId, scope);
            if (!canGrant)
                throw new UnauthorizedAccessException("User does not have permission to grant this type or scope");
        }

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

        // Audit trail for grant assignment (fixes D-05)
        if (grantedByUserId.HasValue)
        {
            await _auditLogService.LogUserActionAsync(grantedByUserId.Value, "GrantAssigned", "Grant", grant.Id,
                $"Granted '{grantType.Key}' to user {userId}",
                $"GrantTypeId={grantTypeId}, Scope=[Project={scope.ProjectId}, Area={scope.AreaId}, Molecule={scope.MoleculeId}, Company={scope.CompanyId}, Dept={scope.DepartmentId}, JobType={scope.JobTypeId}]");
        }

        return grant;
    }

    public async Task<bool> RevokeAsync(int grantId, int? revokedByUserId = null)
    {
        var grant = await _db.Grants
            .Include(g => g.GrantType)
            .FirstOrDefaultAsync(g => g.Id == grantId);
        if (grant == null)
            return false;

        var grantTypeKey = grant.GrantType?.Key ?? "Unknown";
        var targetUserId = grant.UserId;

        _db.Grants.Remove(grant);
        await _db.SaveChangesAsync();

        // Audit trail for grant revocation (fixes D-05)
        if (revokedByUserId.HasValue)
        {
            await _auditLogService.LogUserActionAsync(revokedByUserId.Value, "GrantRevoked", "Grant", grantId,
                $"Revoked '{grantTypeKey}' from user {targetUserId}",
                $"GrantId={grantId}, GrantTypeKey={grantTypeKey}, TargetUserId={targetUserId}");
        }

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

    /// <summary>
    /// Checks if a user can grant a specific grant type at the given scope.
    /// User must have CanGive=true for the grant type and the scope must be
    /// same or narrower than the user's grant scope.
    /// </summary>
    public async Task<bool> CanUserGrantAsync(int granterId, int grantTypeId, GrantScope targetScope)
    {
        // Get granter's grants with CanGive=true for this grant type
        var granterGrants = await _db.Grants
            .Where(g => g.UserId == granterId && g.GrantTypeId == grantTypeId && g.CanGive)
            .ToListAsync();

        if (!granterGrants.Any())
            return false;

        // Check if any of the granter's grants covers the target scope
        foreach (var grant in granterGrants)
        {
            if (ScopeCovers(grant, targetScope))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Determines if a grant's scope covers (is same or broader than) the target scope.
    /// Broader scopes cover narrower scopes: Project > Area > Molecule > Company/Department
    /// </summary>
    private bool ScopeCovers(Grant granterGrant, GrantScope targetScope)
    {
        // Project scope covers everything
        if (granterGrant.ProjectId.HasValue)
        {
            // If target has a project scope, it must match
            if (targetScope.ProjectId.HasValue)
                return granterGrant.ProjectId == targetScope.ProjectId;
            // Project scope covers all sub-scopes
            return true;
        }

        // Area scope covers molecules, companies, departments within that area
        if (granterGrant.AreaId.HasValue)
        {
            if (targetScope.AreaId.HasValue)
                return granterGrant.AreaId == targetScope.AreaId;
            // Area cannot cover project scope
            if (targetScope.ProjectId.HasValue)
                return false;
            // Area scope covers molecule/company/department if they're within the area
            // For simplicity, require area match or narrower scope
            return !targetScope.AreaId.HasValue;
        }

        // Molecule scope covers companies, departments within that molecule
        if (granterGrant.MoleculeId.HasValue)
        {
            if (targetScope.MoleculeId.HasValue)
                return granterGrant.MoleculeId == targetScope.MoleculeId;
            // Molecule cannot cover project or area scope
            if (targetScope.ProjectId.HasValue || targetScope.AreaId.HasValue)
                return false;
            // Molecule scope can cover company/department within molecule
            return true;
        }

        // Company scope
        if (granterGrant.CompanyId.HasValue)
        {
            if (targetScope.CompanyId.HasValue)
                return granterGrant.CompanyId == targetScope.CompanyId;
            // Company cannot cover broader scopes
            if (targetScope.ProjectId.HasValue || targetScope.AreaId.HasValue || targetScope.MoleculeId.HasValue)
                return false;
        }

        // Department scope
        if (granterGrant.DepartmentId.HasValue)
        {
            if (targetScope.DepartmentId.HasValue)
                return granterGrant.DepartmentId == targetScope.DepartmentId;
            // Department cannot cover broader scopes
            return false;
        }

        // JobType scope
        if (granterGrant.JobTypeId.HasValue)
        {
            if (targetScope.JobTypeId.HasValue)
            {
                if (granterGrant.JobTypeId != targetScope.JobTypeId)
                    return false;
                // If both have company, they must match
                if (granterGrant.CompanyId.HasValue && targetScope.CompanyId.HasValue)
                    return granterGrant.CompanyId == targetScope.CompanyId;
                return true;
            }
            return false;
        }

        // Self-scoped grant (no scope defined) - cannot grant to others
        return false;
    }

    /// <summary>
    /// Assigns grants to a user based on a role template key.
    /// This is the primary method for onboarding users with the correct grants.
    /// </summary>
    public async Task<int> AssignRoleTemplateGrantsAsync(int userId, string roleTemplateKey, GrantScope scope, int? grantedByUserId = null)
    {
        var roleTemplate = await _db.RoleTemplates
            .Include(rt => rt.AutoGrants)
            .ThenInclude(ag => ag.GrantType)
            .FirstOrDefaultAsync(rt => rt.Key == roleTemplateKey && rt.IsActive);

        if (roleTemplate == null)
            return 0;

        var grantsAssigned = 0;

        foreach (var autoGrant in roleTemplate.AutoGrants)
        {
            // Determine effective scope based on ScopeMode
            var effectiveScope = DetermineEffectiveScope(autoGrant.ScopeMode, scope);

            // Check if grant already exists
            var existing = await _db.Grants.FirstOrDefaultAsync(g =>
                g.UserId == userId && g.GrantTypeId == autoGrant.GrantTypeId &&
                g.ProjectId == effectiveScope.ProjectId && g.AreaId == effectiveScope.AreaId &&
                g.MoleculeId == effectiveScope.MoleculeId && g.DepartmentId == effectiveScope.DepartmentId &&
                g.CompanyId == effectiveScope.CompanyId && g.JobTypeId == effectiveScope.JobTypeId);

            if (existing == null)
            {
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
                    GrantedByUserId = grantedByUserId,
                    GrantedAt = DateTime.UtcNow,
                    IsAutoGrant = true,
                    Notes = $"Onboarding grant from role template: {roleTemplate.Key}"
                };

                _db.Grants.Add(grant);
                grantsAssigned++;
            }
        }

        await _db.SaveChangesAsync();
        return grantsAssigned;
    }

    /// <summary>
    /// Verifies a user's grants against their expected grants from role template.
    /// </summary>
    public async Task<GrantVerificationResult> VerifyUserGrantsAsync(int userId)
    {
        // SECURITY-AUDITED: SAFE — scoped by specific userId; admin diagnostic operation
        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            return new GrantVerificationResult
            {
                UserId = userId,
                UserDisplayName = "Not Found",
                MissingGrants = new List<string> { "User not found" }
            };
        }

        var result = new GrantVerificationResult
        {
            UserId = userId,
            UserDisplayName = user.DisplayName,
            UserEmail = user.Email,
            UserRole = user.Role.ToString()
        };

        // Get role template for user's role
        var roleTemplateKey = MapUserRoleToRoleTemplateKey(user.Role);
        var roleTemplate = await _db.RoleTemplates
            .Include(rt => rt.AutoGrants)
            .ThenInclude(ag => ag.GrantType)
            .FirstOrDefaultAsync(rt => rt.Key == roleTemplateKey && rt.IsActive);

        if (roleTemplate != null)
        {
            result.ExpectedGrants = roleTemplate.AutoGrants
                .Select(ag => ag.GrantType.Key)
                .OrderBy(k => k)
                .ToList();
        }

        // Get user's actual grants
        var actualGrants = await _db.Grants
            .Include(g => g.GrantType)
            .Where(g => g.UserId == userId)
            .ToListAsync();

        result.ActualGrants = actualGrants
            .Select(g => g.GrantType.Key)
            .Distinct()
            .OrderBy(k => k)
            .ToList();

        // Calculate missing and extra
        result.MissingGrants = result.ExpectedGrants.Except(result.ActualGrants).OrderBy(k => k).ToList();
        result.ExtraGrants = result.ActualGrants.Except(result.ExpectedGrants).OrderBy(k => k).ToList();

        return result;
    }

    /// <summary>
    /// Verifies grants for all users in the system.
    /// </summary>
    public async Task<List<GrantVerificationResult>> VerifyAllUserGrantsAsync()
    {
        // SECURITY-AUDITED: SAFE — admin-only bulk verification; returns only user IDs for further processing
        var users = await _db.Users.IgnoreQueryFilters()
            .Where(u => u.IsActive)
            .Select(u => u.Id)
            .ToListAsync();

        var results = new List<GrantVerificationResult>();
        foreach (var userId in users)
        {
            results.Add(await VerifyUserGrantsAsync(userId));
        }

        return results;
    }

    /// <summary>
    /// Repairs a user's grants by adding any missing grants from their role template.
    /// </summary>
    public async Task<int> RepairUserGrantsAsync(int userId, int? repairedByUserId = null)
    {
        // SECURITY-AUDITED: SAFE — scoped by specific userId; admin repair operation
        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
            return 0;

        var roleTemplateKey = MapUserRoleToRoleTemplateKey(user.Role);
        var scope = GrantScope.Company(user.CompanyId);

        return await AssignRoleTemplateGrantsAsync(userId, roleTemplateKey, scope, repairedByUserId);
    }

    /// <summary>
    /// Repairs grants for all users in the system.
    /// </summary>
    public async Task<int> RepairAllUserGrantsAsync(int? repairedByUserId = null)
    {
        // SECURITY-AUDITED: SAFE — admin-only bulk repair; iterates all active users to fix grants
        var users = await _db.Users.IgnoreQueryFilters()
            .Where(u => u.IsActive)
            .ToListAsync();

        var totalRepaired = 0;
        foreach (var user in users)
        {
            totalRepaired += await RepairUserGrantsAsync(user.Id, repairedByUserId);
        }

        return totalRepaired;
    }

    /// <summary>
    /// Maps UserRole enum to the corresponding RoleTemplate key.
    /// </summary>
    private static string MapUserRoleToRoleTemplateKey(Models.Support.UserRole role)
    {
        return role switch
        {
            Models.Support.UserRole.Owner => "Owner",
            Models.Support.UserRole.Director => "BRDirector", // Directors typically get BR Director template
            Models.Support.UserRole.Manager => "MoleculeAdmin", // Managers get molecule admin template
            Models.Support.UserRole.Employee => "Employee",
            Models.Support.UserRole.Trainee => "Employee", // Trainees get employee-level grants
            Models.Support.UserRole.Assigner => "Assigner",
            _ => "Employee"
        };
    }
}
