using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — grant system requires cross-company visibility
// for Directors managing hierarchies; lookups scoped by explicit userId/grantTypeId parameters
public class GrantService : IGrantService
{
    private readonly AppDbContext _db;
    private readonly IHierarchyService _hierarchyService;
    private readonly IAuditLogService _auditLogService;
    // Optional logger — production DI injects the concrete logger; tests pass null and the
    // null-conditional log calls become no-ops. Keeps the test surface untouched while
    // enabling diagnostic logging for the silent-no-op paths discovered in the 2026-05-11
    // GrantService audit (e.g. RoleTemplate not found → user gets no auto-grants).
    private readonly ILogger<GrantService>? _logger;

    // C-09: Per-request cache for hierarchy contexts and grant type lookups
    // GrantService is scoped (one per HTTP request), so this caches for the request lifetime
    private readonly Dictionary<int, UserHierarchyContext?> _hierarchyCache = new();
    private readonly Dictionary<string, GrantType?> _grantTypeCache = new();

    public GrantService(
        AppDbContext db,
        IHierarchyService hierarchyService,
        IAuditLogService auditLogService,
        ILogger<GrantService>? logger = null)
    {
        _db = db;
        _hierarchyService = hierarchyService;
        _auditLogService = auditLogService;
        _logger = logger;
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

    public async Task<bool> HasCalendarEditPermissionAsync(int userId)
    {
        return await HasGrantAsync(userId, "AdminAccess")
            || await HasGrantAsync(userId, "AssignAlhutShifts")
            || await HasGrantAsync(userId, "AssignTextShifts")
            || await HasGrantAsync(userId, "AssignBRShifts")
            || await HasGrantAsync(userId, "AssignTechShifts")
            || await HasGrantAsync(userId, "AssignChores")
            || await HasGrantAsync(userId, "ManageOnDuty")
            || await HasGrantAsync(userId, "EditOnCallCalendar");
    }

    public async Task<bool> HasCalendarNotePermissionAsync(int userId)
    {
        return await HasGrantAsync(userId, "WriteOverviewNotes")
            || await HasCalendarEditPermissionAsync(userId);
    }

    public async Task<bool> CanReachUserForNoteAsync(int callerId, int targetUserId)
    {
        // Self-target is always allowed — every user can note their own row.
        if (callerId == targetUserId) return true;

        var targetUser = await _db.Users.IgnoreQueryFilters()
            .Where(u => u.Id == targetUserId && u.IsActive)
            .Select(u => new { u.CompanyId })
            .FirstOrDefaultAsync();
        if (targetUser == null) return false;

        // Non-self writes require an actual assign grant — Employees/Trainees (note-only tier)
        // can only write on their own row. EditOnCallCalendar (grant 131) is explicitly NOT
        // counted here: it's a scoped on-call editing grant, not a general note-write elevator.
        if (!await HasAnyAssignGrantAsync(callerId)) return false;

        // Assigner/Lead/BRDirector/Director/MoleculeAdmin/AreaAdmin/Owner tier:
        // accessible-company set from their actual assign grants decides cross-company reach.
        var assignGrantKeys = new[]
        {
            "AssignAlhutShifts", "AssignTextShifts", "AssignBRShifts", "AssignTechShifts",
            "AssignChores", "ManageOnDuty"
        };
        var accessibleCompanies = new HashSet<int>();
        foreach (var key in assignGrantKeys)
        {
            var companies = await GetAccessibleCompanyIdsForGrantAsync(callerId, key);
            foreach (var c in companies) accessibleCompanies.Add(c);
        }
        return accessibleCompanies.Contains(targetUser.CompanyId);
    }

    // Private helper: true if user has any actual assignment grant (not just EditOnCallCalendar).
    // Used as the manager-tier gate for note-writing scope.
    private async Task<bool> HasAnyAssignGrantAsync(int userId)
    {
        return await HasGrantAsync(userId, "AdminAccess")
            || await HasGrantAsync(userId, "AssignAlhutShifts")
            || await HasGrantAsync(userId, "AssignTextShifts")
            || await HasGrantAsync(userId, "AssignBRShifts")
            || await HasGrantAsync(userId, "AssignTechShifts")
            || await HasGrantAsync(userId, "AssignChores")
            || await HasGrantAsync(userId, "ManageOnDuty");
    }

    public async Task<bool> HasGrantAsync(int userId, string grantKey, GrantScope scope)
    {
        return await HasGrantWithScopeAsync(userId, grantKey,
            scope.ProjectId, scope.AreaId, scope.MoleculeId,
            scope.DepartmentId, scope.CompanyId, scope.JobTypeId);
    }

    public async Task<bool> HasGrantWithScopeAsync(int userId, string grantKey, int? projectId = null, int? areaId = null,
        int? moleculeId = null, int? departmentId = null, int? companyId = null, int? jobTypeId = null,
        int? targetUserId = null)
    {
        var grantType = await GetGrantTypeByKeyAsync(grantKey);
        if (grantType == null)
            return false;

        // C-09: Cache hierarchy context per-request to avoid repeated multi-join queries
        if (!_hierarchyCache.TryGetValue(userId, out var userContext))
        {
            userContext = await _hierarchyService.GetUserHierarchyContextAsync(userId);
            _hierarchyCache[userId] = userContext;
        }

        // Check for exact scope match or higher-level scope that includes this scope
        var grants = await _db.Grants
            .Where(g => g.UserId == userId && g.GrantTypeId == grantType.Id && g.CanOwn)
            .ToListAsync();

        // Pre-resolve the REQUESTED scope's hierarchy so each grant in the loop can do proper
        // cascade matching (e.g., grant at Area X covers a moleculeId Y when Y is in X). Without
        // this, callers had to fall back to brittle user-own-path equality checks that mishandled
        // cross-molecule queries.
        int? requestedMoleculeArea = null;
        int? requestedCompanyMolecule = null;
        int? requestedCompanyArea = null;
        if (moleculeId.HasValue)
        {
            requestedMoleculeArea = await _db.Molecules.IgnoreQueryFilters()
                .Where(m => m.Id == moleculeId.Value)
                .Select(m => (int?)m.AreaId)
                .FirstOrDefaultAsync();
        }
        if (companyId.HasValue)
        {
            var cmp = await _db.Companies.IgnoreQueryFilters()
                .Where(c => c.Id == companyId.Value && c.Molecule != null)
                .Select(c => new { c.MoleculeId, AreaId = (int?)c.Molecule!.AreaId })
                .FirstOrDefaultAsync();
            if (cmp != null)
            {
                requestedCompanyMolecule = cmp.MoleculeId;
                requestedCompanyArea = cmp.AreaId;
            }
        }

        foreach (var grant in grants)
        {
            // A-10: Self scope — match when target user equals the requesting user
            if (!grant.ProjectId.HasValue && !grant.AreaId.HasValue && !grant.MoleculeId.HasValue &&
                !grant.DepartmentId.HasValue && !grant.CompanyId.HasValue && !grant.JobTypeId.HasValue)
            {
                // Self-scoped grant: valid only when accessing own resources
                if (targetUserId.HasValue && targetUserId.Value == userId)
                    return true;
                continue;
            }

            // Project scope covers everything below
            if (grant.ProjectId.HasValue)
            {
                // When no specific scope is requested, validate the user's own hierarchy
                // falls within the grant's project scope. This prevents cross-project access
                // when calling code passes no scope params.
                if (!projectId.HasValue && !areaId.HasValue && !moleculeId.HasValue &&
                    !departmentId.HasValue && !companyId.HasValue && !jobTypeId.HasValue)
                {
                    if (userContext != null && grant.ProjectId == userContext.Path.Project.Id)
                        return true;
                    continue;
                }

                if (projectId.HasValue && grant.ProjectId == projectId)
                    return true;
                // Project scope also covers area, molecule, etc. if they belong to this project
                if (userContext != null && grant.ProjectId == userContext.Path.Project.Id)
                    return true;
            }

            // SECURITY-AUDITED 2026-05-07: JobType filter for area/molecule-scoped grants.
            // When the grant is JobType-restricted (e.g., AssignTextShifts stored with
            // JobTypeId=Alhut for an Alhut Lead via useOwnJobType:true), the request must
            // satisfy the JobType to match. Mismatch → skip this grant.
            //
            // When the request omits jobTypeId, we preserve legacy behavior (the molecule/area
            // match still applies) — most page-level [Authorize(Policy=...)] checks don't pass
            // jobTypeId, and the existing JobType-with-Company branch below has been the
            // only enforcement point. Tightening the no-jobTypeId case would silently break
            // 22+ ETM-OWN grants across Lead/Director templates without a coordinated audit.
            //
            // Callers that DO need JobType enforcement (e.g., shift-assign handlers) must
            // explicitly pass jobTypeId. See Pages/Calendar/Table.cshtml.cs:OnPostAssignEmployeeAsync.
            bool jobTypeMismatch = grant.JobTypeId.HasValue && jobTypeId.HasValue
                                   && grant.JobTypeId != jobTypeId;

            // Area scope: explicit area match, OR cascade — the requested moleculeId/companyId
            // belongs to this area (verified via the pre-resolved hierarchy lookup above), OR
            // the grant is in the user's own area and no narrower scope was requested.
            // Bug-fix 2026-04-25: previously the user-path fallback ran unconditionally,
            // letting Lead/Director/Kabar at area X be reported as having grants for any
            // sibling molecule Y in area X regardless of which molecule the caller asked about.
            if (grant.AreaId.HasValue && !jobTypeMismatch)
            {
                if (areaId.HasValue && grant.AreaId == areaId)
                    return true;
                if (requestedMoleculeArea.HasValue && grant.AreaId == requestedMoleculeArea)
                    return true;
                if (requestedCompanyArea.HasValue && grant.AreaId == requestedCompanyArea)
                    return true;
                if (userContext != null && grant.AreaId == userContext.Path.Area.Id
                    && !areaId.HasValue && !moleculeId.HasValue
                    && !companyId.HasValue && !departmentId.HasValue && !jobTypeId.HasValue)
                    return true;
            }

            // Molecule scope: explicit molecule match, OR cascade — the requested companyId
            // belongs to this molecule, OR no narrower scope and grant is at user's own molecule.
            if (grant.MoleculeId.HasValue && !jobTypeMismatch)
            {
                if (moleculeId.HasValue && grant.MoleculeId == moleculeId)
                    return true;
                if (requestedCompanyMolecule.HasValue && grant.MoleculeId == requestedCompanyMolecule)
                    return true;
                if (userContext != null && grant.MoleculeId == userContext.Path.Molecule.Id
                    && !moleculeId.HasValue
                    && !companyId.HasValue && !departmentId.HasValue && !jobTypeId.HasValue)
                    return true;
            }

            // Department scope
            if (grant.DepartmentId.HasValue && departmentId.HasValue && grant.DepartmentId == departmentId)
                return true;

            // Company scope — only matches if grant has NO JobType restriction
            // (Grants with both CompanyId and JobTypeId are handled in the combined check below)
            if (grant.CompanyId.HasValue && companyId.HasValue && grant.CompanyId == companyId
                && !grant.JobTypeId.HasValue)
                return true;

            // JobType scope (optionally combined with company)
            // Phase 2: Targeted grants (e.g., BRDirector ApproveVacations for BR/Hakam)
            // require both company AND jobtype to match
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
        // C-01: Cache GrantType lookups to reduce N+1 queries per authorization check
        if (_grantTypeCache.TryGetValue(key, out var cached))
            return cached;

        var result = await _db.GrantTypes
            .FirstOrDefaultAsync(gt => gt.Key == key && gt.IsActive);
        _grantTypeCache[key] = result;
        return result;
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
                    .Where(c => c.Molecule!.Area!.ProjectId == grant.ProjectId.Value)
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
                    .Where(c => c.Molecule!.AreaId == grant.AreaId.Value)
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
            if (userContext?.Path.Company != null)
            {
                companyIds.Add(userContext.Path.Company.Id);
            }
        }

        return companyIds.ToList();
    }

    public async Task<List<int>> GetAccessibleMoleculeIdsForGrantAsync(int userId, string grantKey)
    {
        var grantType = await GetGrantTypeByKeyAsync(grantKey);
        if (grantType == null)
            return new List<int>();

        var userContext = await _hierarchyService.GetUserHierarchyContextAsync(userId);

        var grants = await _db.Grants
            .Where(g => g.UserId == userId && g.GrantTypeId == grantType.Id && g.CanOwn)
            .ToListAsync();

        if (!grants.Any())
            return new List<int>();

        var moleculeIds = new HashSet<int>();

        foreach (var grant in grants)
        {
            // Project scope - all molecules in project
            if (grant.ProjectId.HasValue)
            {
                // SECURITY-AUDITED: SAFE — scoped by grant's ProjectId; resolves molecules within granted project scope
                var projectMoleculeIds = await _db.Molecules
                    .IgnoreQueryFilters()
                    .Where(m => m.Area!.ProjectId == grant.ProjectId.Value && m.IsActive)
                    .Select(m => m.Id)
                    .ToListAsync();
                foreach (var id in projectMoleculeIds)
                    moleculeIds.Add(id);
                continue;
            }

            // Area scope - all molecules in area
            if (grant.AreaId.HasValue)
            {
                // SECURITY-AUDITED: SAFE — scoped by grant's AreaId; resolves molecules within granted area scope
                var areaMoleculeIds = await _db.Molecules
                    .IgnoreQueryFilters()
                    .Where(m => m.AreaId == grant.AreaId.Value && m.IsActive)
                    .Select(m => m.Id)
                    .ToListAsync();
                foreach (var id in areaMoleculeIds)
                    moleculeIds.Add(id);
                continue;
            }

            // Molecule scope - just that molecule
            if (grant.MoleculeId.HasValue)
            {
                moleculeIds.Add(grant.MoleculeId.Value);
                continue;
            }

            // Company scope - that company's molecule
            if (grant.CompanyId.HasValue)
            {
                // SECURITY-AUDITED: SAFE — scoped by grant's CompanyId; resolves to the company's molecule
                var companyMoleculeId = await _db.Companies
                    .IgnoreQueryFilters()
                    .Where(c => c.Id == grant.CompanyId.Value && c.MoleculeId.HasValue)
                    .Select(c => c.MoleculeId!.Value)
                    .FirstOrDefaultAsync();
                if (companyMoleculeId > 0)
                    moleculeIds.Add(companyMoleculeId);
                continue;
            }

            // Self scope (no scope defined) - user's own molecule
            if (userContext?.Path.Molecule != null)
            {
                moleculeIds.Add(userContext.Path.Molecule.Id);
            }
        }

        return moleculeIds.ToList();
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

    public async Task<bool> RevokeAllUserGrantsAsync(int userId, int? revokedByUserId = null)
    {
        var grants = await _db.Grants.Where(g => g.UserId == userId).ToListAsync();
        var grantCount = grants.Count;
        _db.Grants.RemoveRange(grants);
        await _db.SaveChangesAsync();

        // Audit trail for bulk grant revocation
        if (revokedByUserId.HasValue && grantCount > 0)
        {
            await _auditLogService.LogUserActionAsync(revokedByUserId.Value, "AllGrantsRevoked", "Grant", null,
                $"Revoked all {grantCount} grants from user {userId}",
                $"TargetUserId={userId}, GrantCount={grantCount}");
        }

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
        {
            // Silent no-op would leave the user with degraded permissions and zero diagnostic
            // trail. Log a warning so admins can see when grant assignment was skipped because
            // the role template referenced by RoleTemplateId no longer exists (e.g. template
            // deleted in admin UI while users still reference it, or seed-data drift).
            _logger?.LogWarning(
                "ApplyAutoGrantsAsync skipped: RoleTemplate {RoleTemplateId} not found in DB for user {UserId}. " +
                "User will have no auto-grants applied. Check RoleTemplate seed/admin state.",
                roleTemplateId, userId);
            return;
        }

        // Phase 1: Resolve all expected grants from template
        var expectedGrants = new List<(int GrantTypeId, GrantScope EffectiveScope, bool CanOwn, bool CanGive)>();

        foreach (var autoGrant in roleTemplate.AutoGrants)
        {
            // Step 1: Resolve JobTypeId from TargetJobTypeId / UseOwnJobType
            int? resolvedJobTypeId = null;
            if (autoGrant.TargetJobTypeId != null)
                resolvedJobTypeId = autoGrant.TargetJobTypeId;
            else if (autoGrant.UseOwnJobType)
                resolvedJobTypeId = roleScope.JobTypeId;
            // else: null = all jobtypes

            // Step 2: Determine effective scope (extracts ONE level from roleScope)
            var effectiveScope = DetermineEffectiveScope(autoGrant.ScopeMode, roleScope);

            // Step 3: Overlay resolved JobTypeId
            effectiveScope = effectiveScope with { JobTypeId = resolvedJobTypeId };

            expectedGrants.Add((autoGrant.GrantTypeId, effectiveScope, autoGrant.CanOwn, autoGrant.CanGive));
        }

        // Phase 2: Clean up stale auto-grants (JobType changed, template modified, etc.)
        var existingAutoGrants = await _db.Grants
            .Where(g => g.UserId == userId && g.IsAutoGrant &&
                   g.Notes != null && g.Notes.Contains($"role: {roleTemplate.Key}"))
            .ToListAsync();

        foreach (var existing in existingAutoGrants)
        {
            var stillExpected = expectedGrants.Any(e =>
                e.GrantTypeId == existing.GrantTypeId &&
                e.EffectiveScope.ProjectId == existing.ProjectId &&
                e.EffectiveScope.AreaId == existing.AreaId &&
                e.EffectiveScope.MoleculeId == existing.MoleculeId &&
                e.EffectiveScope.DepartmentId == existing.DepartmentId &&
                e.EffectiveScope.CompanyId == existing.CompanyId &&
                e.EffectiveScope.JobTypeId == existing.JobTypeId);

            if (!stillExpected)
            {
                _db.Grants.Remove(existing);
            }
        }

        // Phase 3: Insert new grants (dedup includes JobTypeId)
        // Pre-load all existing grants for user in one query (avoids N+1)
        var allUserGrants = await _db.Grants
            .Where(g => g.UserId == userId)
            .Select(g => new { g.GrantTypeId, g.ProjectId, g.AreaId, g.MoleculeId, g.DepartmentId, g.CompanyId, g.JobTypeId })
            .ToListAsync();

        foreach (var (grantTypeId, effectiveScope, canOwn, canGive) in expectedGrants)
        {
            var alreadyExists = allUserGrants.Any(g =>
                g.GrantTypeId == grantTypeId &&
                g.ProjectId == effectiveScope.ProjectId && g.AreaId == effectiveScope.AreaId &&
                g.MoleculeId == effectiveScope.MoleculeId && g.DepartmentId == effectiveScope.DepartmentId &&
                g.CompanyId == effectiveScope.CompanyId && g.JobTypeId == effectiveScope.JobTypeId);

            if (!alreadyExists)
            {
                _db.Grants.Add(new Grant
                {
                    UserId = userId,
                    GrantTypeId = grantTypeId,
                    ProjectId = effectiveScope.ProjectId,
                    AreaId = effectiveScope.AreaId,
                    MoleculeId = effectiveScope.MoleculeId,
                    DepartmentId = effectiveScope.DepartmentId,
                    CompanyId = effectiveScope.CompanyId,
                    JobTypeId = effectiveScope.JobTypeId,
                    CanOwn = canOwn,
                    CanGive = canGive,
                    GrantedAt = DateTime.UtcNow,
                    IsAutoGrant = true,
                    Notes = $"Auto-granted from role: {roleTemplate.Key}"
                });
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

    /// <summary>
    /// Extracts exactly ONE scope level from the full hierarchy roleScope.
    /// CRITICAL: SAR must extract ONLY CompanyId+DepartmentId — NOT the full hierarchy.
    /// Reason: GetAccessibleCompanyIdsForGrantAsync cascades broadest→narrowest with continue.
    /// If a Grant has both ProjectId AND CompanyId, ProjectId fires first and returns ALL
    /// companies in the project, massively over-granting an Employee.
    /// </summary>
    private GrantScope DetermineEffectiveScope(GrantScopeMode scopeMode, GrantScope roleScope)
    {
        return scopeMode switch
        {
            // SAR: Company level only — strip all hierarchy above company
            GrantScopeMode.SameAsRole => new GrantScope(
                CompanyId: roleScope.CompanyId,
                DepartmentId: roleScope.DepartmentId
            ),
            GrantScopeMode.ExpandToMolecule => new GrantScope(MoleculeId: roleScope.MoleculeId),
            GrantScopeMode.ExpandToArea => new GrantScope(AreaId: roleScope.AreaId),
            GrantScopeMode.ExpandToProject => new GrantScope(ProjectId: roleScope.ProjectId),
            GrantScopeMode.Custom => roleScope,
            _ => throw new ArgumentOutOfRangeException(nameof(scopeMode), $"Unknown GrantScopeMode: {scopeMode}")
        };
    }

    /// <summary>
    /// Checks if a user can grant a specific grant type at the given scope.
    /// User must have CanGive=true for the grant type and the scope must be
    /// same or narrower than the user's grant scope.
    ///
    /// 2026-05-12 audit fix: pre-resolves the target's hierarchy chain so ScopeCovers
    /// can detect cross-area / cross-molecule over-granting. Without this, an Area-A
    /// granter with CanGive=true could delegate to a target with MoleculeId from Area B,
    /// because ScopeCovers had no way to know which area the target's molecule belonged to.
    /// </summary>
    public async Task<bool> CanUserGrantAsync(int granterId, int grantTypeId, GrantScope targetScope)
    {
        // Get granter's grants with CanGive=true for this grant type
        var granterGrants = await _db.Grants
            .Where(g => g.UserId == granterId && g.GrantTypeId == grantTypeId && g.CanGive)
            .ToListAsync();

        if (!granterGrants.Any())
            return false;

        // Resolve the target's hierarchy chain ONCE, so ScopeCovers can correctly handle
        // the cross-area case (target has only MoleculeId; granter has AreaId — must verify
        // the molecule belongs to the granter's area). Without these pre-resolved values
        // the function previously fell through "for simplicity" and returned true regardless,
        // letting an Area-A granter delegate to a molecule in Area B.
        int? targetMoleculeArea = null;
        int? targetCompanyMolecule = null;
        int? targetCompanyArea = null;
        if (targetScope.MoleculeId.HasValue)
        {
            targetMoleculeArea = await _db.Molecules.IgnoreQueryFilters()
                .Where(m => m.Id == targetScope.MoleculeId.Value)
                .Select(m => (int?)m.AreaId)
                .FirstOrDefaultAsync();
        }
        if (targetScope.CompanyId.HasValue)
        {
            var cmp = await _db.Companies.IgnoreQueryFilters()
                .Where(c => c.Id == targetScope.CompanyId.Value && c.Molecule != null)
                .Select(c => new { c.MoleculeId, AreaId = (int?)c.Molecule!.AreaId })
                .FirstOrDefaultAsync();
            if (cmp != null)
            {
                targetCompanyMolecule = cmp.MoleculeId;
                targetCompanyArea = cmp.AreaId;
            }
        }

        // Check if any of the granter's grants covers the target scope
        foreach (var grant in granterGrants)
        {
            if (ScopeCovers(grant, targetScope, targetMoleculeArea, targetCompanyMolecule, targetCompanyArea))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Determines if a grant's scope covers (is same or broader than) the target scope.
    /// Broader scopes cover narrower scopes: Project > Area > Molecule > Company/Department.
    ///
    /// Pre-resolved hierarchy (targetMoleculeArea, targetCompanyMolecule, targetCompanyArea) is
    /// passed in by CanUserGrantAsync so this method can detect cross-area / cross-molecule
    /// over-granting WITHOUT being async (which would require touching every overload). The
    /// 2026-05-12 audit found that previously this method returned `!targetScope.AreaId.HasValue`
    /// "for simplicity" — which silently let an Area-A granter cover a target whose Molecule
    /// actually belonged to Area B. Same risk for Molecule-scoped granters covering
    /// cross-molecule companies.
    /// </summary>
    private static bool ScopeCovers(
        Grant granterGrant,
        GrantScope targetScope,
        int? targetMoleculeArea = null,
        int? targetCompanyMolecule = null,
        int? targetCompanyArea = null)
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

        // Area scope covers molecules, companies, departments WITHIN THAT AREA.
        if (granterGrant.AreaId.HasValue)
        {
            if (targetScope.AreaId.HasValue)
                return granterGrant.AreaId == targetScope.AreaId;
            // Area cannot cover project scope
            if (targetScope.ProjectId.HasValue)
                return false;
            // Cross-area check via pre-resolved hierarchy: target's Molecule must belong
            // to granter's area; target's Company must belong (via its molecule) to it.
            if (targetScope.MoleculeId.HasValue)
                return targetMoleculeArea.HasValue && targetMoleculeArea == granterGrant.AreaId;
            if (targetScope.CompanyId.HasValue)
                return targetCompanyArea.HasValue && targetCompanyArea == granterGrant.AreaId;
            // Target has Department / JobType only / Self — refuse delegation rather than
            // assume coverage. Admin who really wants to delegate must scope explicitly.
            return false;
        }

        // Molecule scope covers companies, departments WITHIN THAT MOLECULE.
        if (granterGrant.MoleculeId.HasValue)
        {
            if (targetScope.MoleculeId.HasValue)
                return granterGrant.MoleculeId == targetScope.MoleculeId;
            // Molecule cannot cover project or area scope
            if (targetScope.ProjectId.HasValue || targetScope.AreaId.HasValue)
                return false;
            // Cross-molecule check: target's Company must belong to granter's molecule.
            if (targetScope.CompanyId.HasValue)
                return targetCompanyMolecule.HasValue && targetCompanyMolecule == granterGrant.MoleculeId;
            // Target Department / JobType-only / Self — refuse, same rationale as Area branch.
            return false;
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
            // Resolve JobTypeId: TargetJobTypeId (explicit) > UseOwnJobType (user's own) > null (all)
            int? resolvedJobTypeId = null;
            if (autoGrant.TargetJobTypeId != null)
                resolvedJobTypeId = autoGrant.TargetJobTypeId;
            else if (autoGrant.UseOwnJobType)
                resolvedJobTypeId = scope.JobTypeId;

            // Determine effective scope based on ScopeMode, then overlay JobTypeId
            var effectiveScope = DetermineEffectiveScope(autoGrant.ScopeMode, scope) with { JobTypeId = resolvedJobTypeId };

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
        var user = await _db.Users.IgnoreQueryFilters()
            .Include(u => u.JobType)
            .FirstOrDefaultAsync(u => u.Id == userId);
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

        // Get role template for user's role + job type
        var roleTemplateKey = MapUserRoleToRoleTemplateKey(user.Role, user.JobType?.Name);
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
        var user = await _db.Users.IgnoreQueryFilters()
            .Include(u => u.JobType)
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
            return 0;

        var roleTemplateKey = MapUserRoleToRoleTemplateKey(user.Role, user.JobType?.Name);
        var scope = await BuildGrantScopeForUserAsync(user, roleTemplateKey);

        return await AssignRoleTemplateGrantsAsync(userId, roleTemplateKey, scope, repairedByUserId);
    }

    /// <summary>
    /// Builds a FULL hierarchy GrantScope for the user. This is raw material —
    /// DetermineEffectiveScope extracts exactly one level per grant's ScopeMode.
    /// Role-independent: the role-specific behavior is encoded in each grant's ScopeMode.
    /// </summary>
    private async Task<GrantScope> BuildGrantScopeForUserAsync(AppUser user, string roleTemplateKey)
    {
        var company = await _db.Companies
            .IgnoreQueryFilters()
            .Include(c => c.Molecule)
                .ThenInclude(m => m!.Area)
                    .ThenInclude(a => a!.Project)
            .FirstOrDefaultAsync(c => c.Id == user.CompanyId);

        if (company == null)
            return GrantScope.Company(user.CompanyId);

        // Full hierarchy — DetermineEffectiveScope extracts the right level per grant
        return new GrantScope(
            ProjectId: company.Molecule?.Area?.ProjectId,
            AreaId: company.Molecule?.AreaId,
            MoleculeId: company.MoleculeId,
            DepartmentId: user.DepartmentId,
            CompanyId: user.CompanyId,
            JobTypeId: user.JobTypeId
        );
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
    /// Maps UserRole enum + JobType name to the correct RoleTemplate key.
    /// Delegates to centralized RoleTemplateMapper to ensure consistency across codebase.
    /// </summary>
    private static string MapUserRoleToRoleTemplateKey(Models.Support.UserRole role, string? jobTypeName)
        => Helpers.RoleTemplateMapper.MapUserRoleToRoleTemplateKey(role, jobTypeName);
}
