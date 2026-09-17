using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

public sealed class GrantBackfillService : IGrantBackfillService
{
    private readonly AppDbContext _db;
    private readonly IGrantService _grantService;
    private readonly IHierarchyService _hierarchyService;
    private readonly ILogger<GrantBackfillService> _logger;

    public GrantBackfillService(
        AppDbContext db,
        IGrantService grantService,
        IHierarchyService hierarchyService,
        ILogger<GrantBackfillService> logger)
    {
        _db = db;
        _grantService = grantService;
        _hierarchyService = hierarchyService;
        _logger = logger;
    }

    public async Task<BackfillReport> PreviewAsync(int? roleTemplateId = null)
    {
        var report = new BackfillReport();

        var users = await LoadEligibleUsersAsync(roleTemplateId);
        report.UsersScanned = users.Count;

        // Build a map: templateId -> distinct AutoGrant GrantTypeId set.
        // NOTE: materialized client-side — SQLite rejects GroupBy with nested Select.ToList projection (SQL APPLY).
        var templateIds = users.Where(u => u.RoleTemplateId.HasValue)
                               .Select(u => u.RoleTemplateId!.Value).Distinct().ToList();
        var rawTemplateGrants = await _db.RoleTemplateGrants
            .Where(rtg => templateIds.Contains(rtg.RoleTemplateId))
            .Select(rtg => new { rtg.RoleTemplateId, rtg.GrantTypeId })
            .ToListAsync();
        var templateGrants = rawTemplateGrants
            .GroupBy(x => x.RoleTemplateId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.GrantTypeId).Distinct().ToList());

        // Map template Id -> Key (for the report)
        var templateKeys = await _db.RoleTemplates
            .Where(rt => templateIds.Contains(rt.Id))
            .ToDictionaryAsync(rt => rt.Id, rt => rt.Key);

        // Cache GrantType Id -> Key for human-readable report (2026-04-16).
        var grantKeyMap = await _db.GrantTypes
            .ToDictionaryAsync(gt => gt.Id, gt => gt.Key);

        foreach (var u in users)
        {
            var tId = u.RoleTemplateId!.Value;
            if (!templateGrants.TryGetValue(tId, out var expectedTypeIds)) continue;

            var userGrants = await _db.Grants
                .IgnoreQueryFilters()
                .Where(g => g.UserId == u.Id)
                .Select(g => new { g.GrantTypeId, g.ProjectId, g.AreaId, g.MoleculeId, g.DepartmentId, g.CompanyId, g.JobTypeId })
                .ToListAsync();

            var userGrantTypeIds = userGrants.Select(g => g.GrantTypeId).Distinct().ToList();
            var missingIds = expectedTypeIds.Except(userGrantTypeIds).ToList();

            // Grants the user HOLDS but not at the scope their template now declares. Comparing grant
            // types alone hid these, so a scope-mode change reported "0 users missing" and looked like
            // it had not applied. Resolved through the same calculation Execute writes from.
            var expectedGrants = await _grantService.GetExpectedAutoGrantsAsync(tId, await BuildRoleScopeAsync(u));
            var mismatchedIds = expectedGrants
                .Where(e => userGrantTypeIds.Contains(e.GrantTypeId))
                .Where(e => !userGrants.Any(g =>
                    g.GrantTypeId == e.GrantTypeId &&
                    g.ProjectId == e.EffectiveScope.ProjectId &&
                    g.AreaId == e.EffectiveScope.AreaId &&
                    g.MoleculeId == e.EffectiveScope.MoleculeId &&
                    g.DepartmentId == e.EffectiveScope.DepartmentId &&
                    g.CompanyId == e.EffectiveScope.CompanyId &&
                    g.JobTypeId == e.EffectiveScope.JobTypeId))
                .Select(e => e.GrantTypeId)
                .Distinct()
                .ToList();

            if (missingIds.Count == 0 && mismatchedIds.Count == 0) continue;

            if (missingIds.Count > 0)
            {
                report.UsersWithMissingGrants++;
                report.TotalMissingGrantRows += missingIds.Count;
            }
            if (mismatchedIds.Count > 0)
            {
                report.UsersWithScopeMismatch++;
                report.TotalScopeMismatchRows += mismatchedIds.Count;
            }

            report.Entries.Add(new BackfillReportEntry
            {
                UserId = u.Id,
                Email = u.Email,
                DisplayName = u.DisplayName ?? string.Empty,
                RoleTemplateId = tId,
                RoleTemplateKey = templateKeys.GetValueOrDefault(tId, string.Empty),
                MissingGrantCount = missingIds.Count,
                MissingGrantKeys = missingIds
                    .Select(id => grantKeyMap.GetValueOrDefault(id, $"#{id}"))
                    .OrderBy(k => k)
                    .ToList(),
                ScopeMismatchCount = mismatchedIds.Count,
                ScopeMismatchGrantKeys = mismatchedIds
                    .Select(id => grantKeyMap.GetValueOrDefault(id, $"#{id}"))
                    .OrderBy(k => k)
                    .ToList(),
            });
        }

        return report;
    }

    public async Task<SurplusReport> PreviewSurplusAsync(int? roleTemplateId = null)
    {
        var report = new SurplusReport();
        var users = await LoadEligibleUsersAsync(roleTemplateId);
        report.UsersScanned = users.Count;

        // Build template -> distinct AutoGrant GrantTypeId set (same dedup approach as PreviewAsync).
        var templateIds = users.Where(u => u.RoleTemplateId.HasValue)
                               .Select(u => u.RoleTemplateId!.Value).Distinct().ToList();
        var rawTemplateGrants = await _db.RoleTemplateGrants
            .Where(rtg => templateIds.Contains(rtg.RoleTemplateId))
            .Select(rtg => new { rtg.RoleTemplateId, rtg.GrantTypeId })
            .ToListAsync();
        var templateGrants = rawTemplateGrants
            .GroupBy(x => x.RoleTemplateId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.GrantTypeId).Distinct().ToHashSet());

        var templateKeys = await _db.RoleTemplates
            .Where(rt => templateIds.Contains(rt.Id))
            .ToDictionaryAsync(rt => rt.Id, rt => rt.Key);

        var grantKeyMap = await _db.GrantTypes
            .ToDictionaryAsync(gt => gt.Id, gt => gt.Key);

        foreach (var u in users)
        {
            var tId = u.RoleTemplateId!.Value;
            if (!templateGrants.TryGetValue(tId, out var templateSet)) continue;

            // Only auto-grants are candidates for "surplus" — manual admin grants (IsAutoGrant=false) are intentional.
            var userAutoGrantTypeIds = await _db.Grants
                .IgnoreQueryFilters()
                .Where(g => g.UserId == u.Id && g.IsAutoGrant)
                .Select(g => g.GrantTypeId)
                .Distinct()
                .ToListAsync();

            var surplusIds = userAutoGrantTypeIds.Where(id => !templateSet.Contains(id)).ToList();
            if (surplusIds.Count > 0)
            {
                report.UsersWithSurplus++;
                report.TotalSurplusRows += surplusIds.Count;
                report.Entries.Add(new SurplusReportEntry
                {
                    UserId = u.Id,
                    Email = u.Email,
                    DisplayName = u.DisplayName ?? string.Empty,
                    RoleTemplateId = tId,
                    RoleTemplateKey = templateKeys.GetValueOrDefault(tId, string.Empty),
                    SurplusGrantCount = surplusIds.Count,
                    SurplusGrantKeys = surplusIds
                        .Select(id => grantKeyMap.GetValueOrDefault(id, $"#{id}"))
                        .OrderBy(k => k)
                        .ToList(),
                });
            }
        }

        return report;
    }

    public async Task<BackfillResult> ExecuteAsync(int? roleTemplateId, int actingUserId)
    {
        var result = new BackfillResult();
        var users = await LoadEligibleUsersAsync(roleTemplateId);
        result.UsersProcessed = users.Count;

        _logger.LogInformation(
            "AUDIT: Grant back-fill started by actor {ActorId}, template filter {TemplateId}, scanning {Count} users",
            actingUserId, roleTemplateId?.ToString() ?? "ALL", users.Count);

        foreach (var u in users)
        {
            try
            {
                // Identity-based diff, not a row COUNT. A re-scope removes one row and adds another,
                // so "rows after minus rows before" reported a real change as zero.
                var beforeIds = await _db.Grants.IgnoreQueryFilters()
                    .Where(g => g.UserId == u.Id).Select(g => g.Id).ToListAsync();

                var roleScope = await BuildRoleScopeAsync(u);

                await _grantService.ApplyAutoGrantsAsync(u.Id, u.RoleTemplateId!.Value, roleScope);

                var afterIds = await _db.Grants.IgnoreQueryFilters()
                    .Where(g => g.UserId == u.Id).Select(g => g.Id).ToListAsync();

                var inserted = afterIds.Except(beforeIds).Count();
                var removed = beforeIds.Except(afterIds).Count();
                if (inserted > 0 || removed > 0)
                {
                    result.UsersUpdated++;
                    result.TotalGrantsInserted += inserted;
                    result.TotalGrantsRemoved += removed;
                }
            }
            catch (Exception ex)
            {
                result.UsersFailed++;
                result.Errors.Add($"User {u.Id} ({u.Email}): {ex.Message}");
                _logger.LogError(ex, "Back-fill failed for user {UserId}", u.Id);
            }
        }

        _logger.LogInformation(
            "AUDIT: Grant back-fill finished. Processed={Processed}, Updated={Updated}, GrantsInserted={Grants}, GrantsRemoved={Removed}, Failed={Failed}",
            result.UsersProcessed, result.UsersUpdated, result.TotalGrantsInserted, result.TotalGrantsRemoved, result.UsersFailed);

        return result;
    }

    /// <summary>The user's full hierarchy scope — the raw material a template's ScopeMode is applied
    /// to. Shared by Execute and Preview so the dry run judges the same scope Execute would write.</summary>
    private async Task<GrantScope> BuildRoleScopeAsync(Models.AppUser u)
    {
        var hierarchyContext = await _hierarchyService.GetUserHierarchyContextAsync(u.Id);
        return new GrantScope(
            ProjectId: hierarchyContext?.Path.Project?.Id,
            AreaId: hierarchyContext?.Path.Area?.Id,
            MoleculeId: hierarchyContext?.Path.Molecule?.Id,
            DepartmentId: u.DepartmentId,
            CompanyId: u.CompanyId,
            JobTypeId: hierarchyContext?.JobType?.Id
        );
    }

    private async Task<List<Models.AppUser>> LoadEligibleUsersAsync(int? roleTemplateId)
    {
        var query = _db.Users.IgnoreQueryFilters()
            .Where(u => u.IsActive && u.RoleTemplateId.HasValue);

        if (roleTemplateId.HasValue)
        {
            query = query.Where(u => u.RoleTemplateId == roleTemplateId.Value);
        }

        return await query.ToListAsync();
    }
}
