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
            if (!templateGrants.TryGetValue(tId, out var expected)) continue;

            var userGrantTypeIds = await _db.Grants
                .IgnoreQueryFilters()
                .Where(g => g.UserId == u.Id)
                .Select(g => g.GrantTypeId)
                .Distinct()
                .ToListAsync();

            var missingIds = expected.Except(userGrantTypeIds).ToList();
            if (missingIds.Count > 0)
            {
                report.UsersWithMissingGrants++;
                report.TotalMissingGrantRows += missingIds.Count;
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
                });
            }
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
                var beforeCount = await _db.Grants.IgnoreQueryFilters()
                    .CountAsync(g => g.UserId == u.Id);

                var hierarchyContext = await _hierarchyService.GetUserHierarchyContextAsync(u.Id);
                var roleScope = new GrantScope(
                    ProjectId: hierarchyContext?.Path.Project?.Id,
                    AreaId: hierarchyContext?.Path.Area?.Id,
                    MoleculeId: hierarchyContext?.Path.Molecule?.Id,
                    DepartmentId: u.DepartmentId,
                    CompanyId: u.CompanyId,
                    JobTypeId: hierarchyContext?.JobType?.Id
                );

                await _grantService.ApplyAutoGrantsAsync(u.Id, u.RoleTemplateId!.Value, roleScope);

                var afterCount = await _db.Grants.IgnoreQueryFilters()
                    .CountAsync(g => g.UserId == u.Id);

                var added = afterCount - beforeCount;
                if (added > 0)
                {
                    result.UsersUpdated++;
                    result.TotalGrantsInserted += added;
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
            "AUDIT: Grant back-fill finished. Processed={Processed}, Updated={Updated}, GrantsInserted={Grants}, Failed={Failed}",
            result.UsersProcessed, result.UsersUpdated, result.TotalGrantsInserted, result.UsersFailed);

        return result;
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
