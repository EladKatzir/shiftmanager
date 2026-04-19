using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

public class PermissionSimulatorService : IPermissionSimulatorService
{
    private readonly AppDbContext _db;
    private readonly IHierarchyService _hierarchyService;

    public PermissionSimulatorService(AppDbContext db, IHierarchyService hierarchyService)
    {
        _db = db;
        _hierarchyService = hierarchyService;
    }

    public async Task<PermissionSimulationResult> SimulateUserAccessAsync(
        int userId,
        string grantKey,
        int? projectId    = null,
        int? areaId       = null,
        int? moleculeId   = null,
        int? departmentId = null,
        int? companyId    = null,
        int? jobTypeId    = null,
        int? targetUserId = null)
    {
        // IgnoreQueryFilters: owner-only diagnostic tool, cross-tenant reads are intentional
        var grantType = await _db.GrantTypes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(gt => gt.Key == grantKey && gt.IsActive);

        if (grantType == null)
            return new PermissionSimulationResult
            {
                IsGranted     = false,
                FailureKind   = SimFailureKind.GrantKeyNotFound,
                FailureReason = $"Grant key '{grantKey}' does not exist or is inactive",
                MissingGrantKey   = grantKey,
                SuggestionToFix   = "Check the grant key spelling or activate the grant type in the system"
            };

        var userContext = await _hierarchyService.GetUserHierarchyContextAsync(userId);
        if (userContext == null)
            return new PermissionSimulationResult
            {
                IsGranted     = false,
                FailureKind   = SimFailureKind.NoGrantsForUser,
                FailureReason = "User hierarchy context could not be resolved (tech user with no department?)"
            };

        var userContextSummary = BuildContextSummary(userContext);

        // IgnoreQueryFilters: owner-only diagnostic tool, cross-tenant reads are intentional
        var grants = await _db.Grants
            .IgnoreQueryFilters()
            .Where(g => g.UserId == userId && g.GrantTypeId == grantType.Id && g.CanOwn)
            .ToListAsync();

        if (grants.Count == 0)
            return new PermissionSimulationResult
            {
                IsGranted         = false,
                FailureKind       = SimFailureKind.NoGrantsForUser,
                FailureReason     = $"User has no '{grantKey}' grants at all",
                MissingGrantKey   = grantKey,
                SuggestionToFix   = $"Assign the '{grantKey}' grant to this user at the appropriate scope level via the Grants page",
                UserContext       = userContextSummary
            };

        var traces = new List<GrantEvalTrace>();
        var kinds  = new List<SimFailureKind>();
        GrantEvalTrace? matchedTrace = null;

        foreach (var grant in grants)
        {
            var (trace, kind) = await EvaluateGrantAsync(
                grant, userContext, projectId, areaId, moleculeId,
                departmentId, companyId, jobTypeId, targetUserId, userId);
            trace.GrantSource = grant.IsAutoGrant ? "AutoGrant" : "Manual";
            traces.Add(trace);
            if (kind.HasValue) kinds.Add(kind.Value);
            matchedTrace ??= trace.Matched ? trace : null;
        }

        if (matchedTrace != null)
            return new PermissionSimulationResult
            {
                IsGranted      = true,
                MatchedTrace   = matchedTrace,
                EvaluatedGrants = traces,
                UserContext    = userContextSummary
            };

        var failureKind = AggregateFailureKind(kinds);
        return new PermissionSimulationResult
        {
            IsGranted       = false,
            FailureKind     = failureKind,
            FailureReason   = traces.LastOrDefault()?.FailReason ?? "No grants matched the requested scope",
            MissingGrantKey = grantKey,
            SuggestionToFix = BuildSuggestion(failureKind, grantKey, jobTypeId),
            EvaluatedGrants = traces,
            UserContext     = userContextSummary
        };
    }

    public async Task<PermissionSimulationResult> SimulateRoleAccessAsync(
        int roleTemplateId,
        int? scopeJobTypeId,
        int? scopeCompanyId,
        int? scopeMoleculeId,
        int? scopeAreaId,
        string grantKey,
        int? projectId    = null,
        int? areaId       = null,
        int? moleculeId   = null,
        int? departmentId = null,
        int? companyId    = null,
        int? jobTypeId    = null)
    {
        // IgnoreQueryFilters: owner-only diagnostic tool, cross-tenant reads are intentional
        var roleTemplate = await _db.RoleTemplates
            .Include(rt => rt.AutoGrants)
                .ThenInclude(ag => ag.GrantType)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(rt => rt.Id == roleTemplateId);

        if (roleTemplate == null)
            return new PermissionSimulationResult
            {
                IsGranted       = false,
                FailureKind     = SimFailureKind.NoGrantsForUser,
                FailureReason   = $"Role template ID {roleTemplateId} not found",
                IsRoleSimulation = true
            };

        // Resolve project from molecule/area so DetermineEffectiveScope can use ProjectId
        int? scopeProjectId = null;
        if (scopeMoleculeId.HasValue)
        {
            // IgnoreQueryFilters: owner-only diagnostic tool, cross-tenant reads are intentional
            var mol = await _db.Molecules.IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.Id == scopeMoleculeId.Value);
            if (mol != null)
            {
                var ar = await _db.Areas.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(a => a.Id == mol.AreaId);
                if (ar != null)
                {
                    scopeAreaId    ??= ar.Id;
                    scopeProjectId   = ar.ProjectId;
                }
            }
        }
        else if (scopeAreaId.HasValue)
        {
            var ar = await _db.Areas.IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => a.Id == scopeAreaId.Value);
            if (ar != null) scopeProjectId = ar.ProjectId;
        }

        var roleScope = new GrantScope(
            ProjectId:  scopeProjectId,
            AreaId:     scopeAreaId,
            MoleculeId: scopeMoleculeId,
            CompanyId:  scopeCompanyId,
            JobTypeId:  scopeJobTypeId
        );

        // Materialize virtual grants — mirrors ApplyAutoGrantsAsync (GrantService.cs:496–510)
        var virtualGrants = roleTemplate.AutoGrants
            .Where(rtg => rtg.CanOwn && rtg.GrantType?.Key == grantKey)
            .Select(rtg =>
            {
                int? resolvedJt    = rtg.TargetJobTypeId ?? (rtg.UseOwnJobType ? scopeJobTypeId : null);
                var  effectiveScope = DetermineEffectiveScope(rtg.ScopeMode, roleScope) with { JobTypeId = resolvedJt };
                return new Grant
                {
                    GrantTypeId  = rtg.GrantTypeId,
                    ProjectId    = effectiveScope.ProjectId,
                    AreaId       = effectiveScope.AreaId,
                    MoleculeId   = effectiveScope.MoleculeId,
                    DepartmentId = effectiveScope.DepartmentId,
                    CompanyId    = effectiveScope.CompanyId,
                    JobTypeId    = effectiveScope.JobTypeId,
                    CanOwn       = true,
                    CanGive      = rtg.CanGive,
                    IsAutoGrant  = true
                };
            })
            .ToList();

        if (virtualGrants.Count == 0)
            return new PermissionSimulationResult
            {
                IsGranted        = false,
                FailureKind      = SimFailureKind.NoGrantsForUser,
                FailureReason    = $"Role template '{roleTemplate.Key}' does not include grant '{grantKey}' (or no CanOwn entries)",
                MissingGrantKey  = grantKey,
                SuggestionToFix  = $"The role template does not grant '{grantKey}'. Check the role template configuration.",
                IsRoleSimulation = true,
                SimulatedRoleName = roleTemplate.Key
            };

        var traces = new List<GrantEvalTrace>();
        var kinds  = new List<SimFailureKind>();
        GrantEvalTrace? matchedTrace = null;

        foreach (var grant in virtualGrants)
        {
            // No real user context for role simulation
            var (trace, kind) = await EvaluateGrantAsync(
                grant, null, projectId, areaId, moleculeId,
                departmentId, companyId, jobTypeId, null, 0);
            trace.GrantSource = $"AutoGrant ({roleTemplate.Key})";
            traces.Add(trace);
            if (kind.HasValue) kinds.Add(kind.Value);
            matchedTrace ??= trace.Matched ? trace : null;
        }

        if (matchedTrace != null)
            return new PermissionSimulationResult
            {
                IsGranted         = true,
                MatchedTrace      = matchedTrace,
                EvaluatedGrants   = traces,
                IsRoleSimulation  = true,
                SimulatedRoleName = roleTemplate.Key
            };

        var failureKind = AggregateFailureKind(kinds);
        return new PermissionSimulationResult
        {
            IsGranted         = false,
            FailureKind       = failureKind,
            FailureReason     = traces.LastOrDefault()?.FailReason ?? "No virtual grants matched the requested scope",
            MissingGrantKey   = grantKey,
            SuggestionToFix   = BuildSuggestion(failureKind, grantKey, jobTypeId),
            EvaluatedGrants   = traces,
            IsRoleSimulation  = true,
            SimulatedRoleName = roleTemplate.Key
        };
    }

    // Mirrors HasGrantWithScopeAsync (GrantService.cs:78–147) with trace output instead of early return
    private async Task<(GrantEvalTrace Trace, SimFailureKind? Kind)> EvaluateGrantAsync(
        Grant grant,
        UserHierarchyContext? userContext,
        int? projectId, int? areaId, int? moleculeId,
        int? departmentId, int? companyId, int? jobTypeId,
        int? targetUserId, int userId)
    {
        var scopeParts = new List<string>();
        if (grant.ProjectId.HasValue)    scopeParts.Add($"ProjectId={grant.ProjectId}");
        if (grant.AreaId.HasValue)       scopeParts.Add($"AreaId={grant.AreaId}");
        if (grant.MoleculeId.HasValue)   scopeParts.Add($"MoleculeId={grant.MoleculeId}");
        if (grant.DepartmentId.HasValue) scopeParts.Add($"DepartmentId={grant.DepartmentId}");
        if (grant.CompanyId.HasValue)    scopeParts.Add($"CompanyId={grant.CompanyId}");
        if (grant.JobTypeId.HasValue)    scopeParts.Add($"JobTypeId={grant.JobTypeId}");
        if (scopeParts.Count == 0)       scopeParts.Add("(self-scoped)");

        var trace = new GrantEvalTrace
        {
            GrantId    = grant.Id,
            ScopeLabel = string.Join(", ", scopeParts),
            CanGive    = grant.CanGive
        };

        // CASE A — all-null scope (self-scoped grant)
        if (!grant.ProjectId.HasValue && !grant.AreaId.HasValue && !grant.MoleculeId.HasValue &&
            !grant.DepartmentId.HasValue && !grant.CompanyId.HasValue && !grant.JobTypeId.HasValue)
        {
            if (!targetUserId.HasValue)
            {
                trace.FailReason = "Self-scoped grant requires targetUserId param — none was passed";
                return (trace, SimFailureKind.SelfScopeNoTarget);
            }
            if (targetUserId.Value == userId)
            {
                trace.Matched = true;
                return (trace, null);
            }
            trace.FailReason = $"Self-scoped grant only covers own resources (userId={userId}, targetUserId={targetUserId.Value})";
            return (trace, SimFailureKind.SelfScopeNotSelf);
        }

        // CASE B — ProjectId set
        if (grant.ProjectId.HasValue)
        {
            if (!projectId.HasValue && !areaId.HasValue && !moleculeId.HasValue &&
                !departmentId.HasValue && !companyId.HasValue && !jobTypeId.HasValue)
            {
                if (userContext != null && grant.ProjectId == userContext.Path.Project.Id)
                {
                    trace.Matched = true;
                    return (trace, null);
                }
                trace.FailReason = $"No scope params passed; user's project ({userContext?.Path.Project.Id}) ≠ grant's project ({grant.ProjectId})";
                return (trace, SimFailureKind.ScopeNotCovered);
            }

            if (projectId.HasValue && grant.ProjectId == projectId)
            {
                trace.Matched = true;
                return (trace, null);
            }
            if (userContext != null && grant.ProjectId == userContext.Path.Project.Id)
            {
                trace.Matched = true;
                return (trace, null);
            }
            trace.FailReason = $"Grant covers ProjectId={grant.ProjectId}; resource is in ProjectId={projectId ?? userContext?.Path.Project.Id}";
            return (trace, SimFailureKind.ScopeNotCovered);
        }

        // CASE C — AreaId set
        if (grant.AreaId.HasValue)
        {
            if (areaId.HasValue && grant.AreaId == areaId)
            {
                trace.Matched = true;
                return (trace, null);
            }
            if (userContext != null && grant.AreaId == userContext.Path.Area.Id)
            {
                trace.Matched = true;
                return (trace, null);
            }
            trace.FailReason = $"Grant covers AreaId={grant.AreaId}; resource area ({areaId ?? userContext?.Path.Area.Id}) does not match";
            return (trace, SimFailureKind.ScopeNotCovered);
        }

        // CASE D — MoleculeId set
        if (grant.MoleculeId.HasValue)
        {
            if (moleculeId.HasValue && grant.MoleculeId == moleculeId)
            {
                trace.Matched = true;
                return (trace, null);
            }
            if (userContext != null && grant.MoleculeId == userContext.Path.Molecule.Id)
            {
                trace.Matched = true;
                return (trace, null);
            }
            trace.FailReason = $"Grant covers MoleculeId={grant.MoleculeId}; resource molecule ({moleculeId ?? userContext?.Path.Molecule.Id}) does not match";
            return (trace, SimFailureKind.ScopeNotCovered);
        }

        // CASE E — DepartmentId set (explicit-only, no implicit self-match)
        if (grant.DepartmentId.HasValue)
        {
            if (departmentId.HasValue && grant.DepartmentId == departmentId)
            {
                trace.Matched = true;
                return (trace, null);
            }
            trace.FailReason = $"Department grants require explicit departmentId param; grant covers DeptId={grant.DepartmentId}, requested={departmentId?.ToString() ?? "null"}";
            return (trace, SimFailureKind.ScopeNotCovered);
        }

        // CASE F — CompanyId set, NO JobTypeId on grant
        if (grant.CompanyId.HasValue && !grant.JobTypeId.HasValue)
        {
            if (!companyId.HasValue)
            {
                trace.FailReason = "Company grant requires caller to pass companyId param; none was provided";
                return (trace, SimFailureKind.ScopeNotCovered);
            }
            if (grant.CompanyId == companyId)
            {
                trace.Matched = true;
                return (trace, null);
            }
            trace.FailReason = $"Grant covers CompanyId={grant.CompanyId}; resource company is CompanyId={companyId}";
            return (trace, SimFailureKind.ScopeNotCovered);
        }

        // CASE G — JobTypeId set on grant
        if (grant.JobTypeId.HasValue)
        {
            if (!jobTypeId.HasValue)
            {
                var jtName = await GetJobTypeNameAsync(grant.JobTypeId.Value);
                trace.FailReason = $"Null-JobType target detected. Grant is restricted to JobType={jtName} (id={grant.JobTypeId}). Tech/global shifts require a grant with no JobType restriction.";
                return (trace, SimFailureKind.NullJobTypeTrap);
            }
            if (grant.JobTypeId == jobTypeId)
            {
                if (!grant.CompanyId.HasValue)
                {
                    trace.Matched = true;
                    return (trace, null);
                }
                if (companyId.HasValue && grant.CompanyId == companyId)
                {
                    trace.Matched = true;
                    return (trace, null);
                }
                var coName = await GetCompanyNameAsync(grant.CompanyId.Value);
                trace.FailReason = $"JobType matches but grant also restricts to Company={coName} (id={grant.CompanyId}); resource company is CompanyId={companyId}";
                return (trace, SimFailureKind.ScopeNotCovered);
            }
            var jtGranted = await GetJobTypeNameAsync(grant.JobTypeId.Value);
            var jtTarget  = await GetJobTypeNameAsync(jobTypeId.Value);
            trace.FailReason = $"Grant restricts to JobType={jtGranted} (id={grant.JobTypeId}); target is JobType={jtTarget} (id={jobTypeId})";
            return (trace, SimFailureKind.JobTypeMismatch);
        }

        trace.FailReason = "Grant could not be evaluated (unexpected scope configuration)";
        return (trace, SimFailureKind.ScopeNotCovered);
    }

    // Copied verbatim from GrantService.cs:601–616
    private static GrantScope DetermineEffectiveScope(GrantScopeMode scopeMode, GrantScope roleScope)
    {
        return scopeMode switch
        {
            GrantScopeMode.SameAsRole       => new GrantScope(CompanyId: roleScope.CompanyId, DepartmentId: roleScope.DepartmentId),
            GrantScopeMode.ExpandToMolecule => new GrantScope(MoleculeId: roleScope.MoleculeId),
            GrantScopeMode.ExpandToArea     => new GrantScope(AreaId: roleScope.AreaId),
            GrantScopeMode.ExpandToProject  => new GrantScope(ProjectId: roleScope.ProjectId),
            GrantScopeMode.Custom           => roleScope,
            _ => throw new ArgumentOutOfRangeException(nameof(scopeMode), $"Unknown GrantScopeMode: {scopeMode}")
        };
    }

    private static SimFailureKind AggregateFailureKind(List<SimFailureKind> kinds)
    {
        if (kinds.Contains(SimFailureKind.NullJobTypeTrap))   return SimFailureKind.NullJobTypeTrap;
        if (kinds.Contains(SimFailureKind.JobTypeMismatch))   return SimFailureKind.JobTypeMismatch;
        if (kinds.Contains(SimFailureKind.SelfScopeNotSelf))  return SimFailureKind.SelfScopeNotSelf;
        if (kinds.Contains(SimFailureKind.SelfScopeNoTarget)) return SimFailureKind.SelfScopeNoTarget;
        return SimFailureKind.ScopeNotCovered;
    }

    private static string BuildSuggestion(SimFailureKind kind, string grantKey, int? targetJobTypeId)
    {
        return kind switch
        {
            SimFailureKind.NoGrantsForUser   => $"Assign the '{grantKey}' grant to this user at the appropriate scope level via the Grants page",
            SimFailureKind.NullJobTypeTrap   => $"Add a '{grantKey}' grant with no JobType restriction at the appropriate scope",
            SimFailureKind.JobTypeMismatch   => $"Add a separate '{grantKey}' grant scoped to JobTypeId={targetJobTypeId}",
            SimFailureKind.SelfScopeNoTarget => "Pass targetUserId when checking self-scoped actions",
            SimFailureKind.SelfScopeNotSelf  => "Self-scoped grants only allow accessing your own resources",
            _                               => $"The user's '{grantKey}' grant does not cover the requested scope. Widen it or add a new grant at the target scope."
        };
    }

    private static UserContextSummary BuildContextSummary(UserHierarchyContext ctx) => new()
    {
        ProjectName  = ctx.Path.Project.Name,
        AreaName     = ctx.Path.Area.Name,
        MoleculeName = ctx.Path.Molecule.Name,
        CompanyName  = ctx.Path.Company?.Name,
        DeptName     = ctx.Path.Department?.Name,
        JobTypeName  = ctx.JobType?.DisplayName
    };

    private async Task<string> GetJobTypeNameAsync(int id)
    {
        var jt = await _db.JobTypes.IgnoreQueryFilters().FirstOrDefaultAsync(j => j.Id == id);
        return jt?.DisplayName ?? jt?.Name ?? id.ToString();
    }

    private async Task<string> GetCompanyNameAsync(int id)
    {
        var co = await _db.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id);
        return co?.Name ?? id.ToString();
    }
}
