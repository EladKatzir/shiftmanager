using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Hubs;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

public class HomeMaterialiserService : IHomeMaterialiserService
{
    private readonly AppDbContext _db;
    private readonly ILogger<HomeMaterialiserService> _logger;
    private readonly IHomeTypeService _homeTypeService;
    private readonly IHubContext<CalendarHub>? _hub;

    public HomeMaterialiserService(
        AppDbContext db,
        ILogger<HomeMaterialiserService> logger,
        IHomeTypeService homeTypeService,
        IHubContext<CalendarHub>? hub = null)
    {
        _db = db;
        _logger = logger;
        _homeTypeService = homeTypeService;
        _hub = hub;
    }

    public async Task SyncMaterialisedHomeRowsAsync(int timeOffRequestId)
    {
        var req = await _db.TimeOffRequests.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == timeOffRequestId);
        if (req == null) return;

        var desired = ComputeDesiredRows(req);

        var existing = await _db.ShiftAssignments.IgnoreQueryFilters()
            .Where(sa => sa.SourceTimeOffRequestId == timeOffRequestId)
            .Include(sa => sa.ShiftInstance).ThenInclude(si => si.ShiftType)
            .ToListAsync();

        // Transaction-aware: if the caller already opened a transaction (e.g.
        // VacationApprovalService.UpdateRequestDatesAsync), participate in it rather than
        // opening a nested one — SQLite/EF throws "The connection is already in a transaction"
        // on a nested BeginTransaction. We only commit/rollback the transaction we own.
        var ownsTransaction = _db.Database.CurrentTransaction == null;
        await using var tx = ownsTransaction ? await _db.Database.BeginTransactionAsync() : null;

        // Delete: existing rows whose (workDate, key) are not in desired
        var desiredKeys = desired.Select(d => (d.WorkDate, d.ShiftTypeKey)).ToHashSet();
        var toDelete = existing
            .Where(sa => !desiredKeys.Contains((sa.ShiftInstance.WorkDate, sa.ShiftInstance.ShiftType.Key)))
            .ToList();
        _db.ShiftAssignments.RemoveRange(toDelete);

        // Insert: desired rows that don't already exist
        var existingKeys = existing
            .Select(sa => (sa.ShiftInstance.WorkDate, sa.ShiftInstance.ShiftType.Key))
            .ToHashSet();
        foreach (var d in desired.Where(d => !existingKeys.Contains((d.WorkDate, d.ShiftTypeKey))))
        {
            // HOME_AM dedup with rotation HOME (§7.4): if the user already has a rotation HOME (no
            // SourceTimeOffRequestId) on this day, skip the HOME_AM materialisation — the full-day
            // HOME row covers it. Only applies to HOME_AM, not the other variants.
            if (d.ShiftTypeKey == ShiftType.KEY_HOME_AM)
            {
                var hasRotationHome = await _db.ShiftAssignments.IgnoreQueryFilters()
                    .Where(sa => sa.UserId == req.UserId
                              && sa.SourceTimeOffRequestId == null
                              && sa.ShiftInstance.WorkDate == d.WorkDate
                              && (sa.ShiftInstance.ShiftType.Key == ShiftType.KEY_HOME
                               || sa.ShiftInstance.ShiftType.Key == ShiftType.KEY_HOME_PM
                               || sa.ShiftInstance.ShiftType.Key == ShiftType.KEY_HOME_AM))
                    .AnyAsync();
                if (hasRotationHome) continue;
            }

            var instanceId = await EnsureInstanceAsync(req.UserId, d.WorkDate, d.ShiftTypeKey);
            _db.ShiftAssignments.Add(new ShiftAssignment
            {
                CompanyId = req.CompanyId,
                ShiftInstanceId = instanceId,
                UserId = req.UserId,
                CreatedAt = DateTime.UtcNow,
                SourceTimeOffRequestId = req.Id
            });
        }

        // Vacation supersedes rotation: when an Approved Vacation covers rotation HOME days,
        // delete those rotation rows (§7.3 first half).
        if (req.Status == RequestStatus.Approved && (req.Type == TimeOffType.Vacation || req.Type == TimeOffType.DayAt))
        {
            var rotationToRemove = await _db.ShiftAssignments.IgnoreQueryFilters()
                .Include(sa => sa.ShiftInstance).ThenInclude(si => si.ShiftType)
                .Where(sa => sa.UserId == req.UserId
                          && sa.SourceTimeOffRequestId == null
                          && sa.ShiftInstance.WorkDate >= req.StartDate
                          && sa.ShiftInstance.WorkDate <= req.EndDate
                          && (sa.ShiftInstance.ShiftType.Key == ShiftType.KEY_HOME
                           || sa.ShiftInstance.ShiftType.Key == ShiftType.KEY_HOME_PM
                           || sa.ShiftInstance.ShiftType.Key == ShiftType.KEY_HOME_AM))
                .ToListAsync();
            _db.ShiftAssignments.RemoveRange(rotationToRemove);
        }

        await _db.SaveChangesAsync();
        if (ownsTransaction) await tx!.CommitAsync();

        _logger.LogInformation("Materialised TimeOffRequest {Id}: {DesiredCount} desired rows", timeOffRequestId, desired.Count);

        // SignalR broadcast (best-effort)
        if (_hub != null)
        {
            try
            {
                var moleculeId = await _db.Companies.IgnoreQueryFilters()
                    .Where(c => c.Id == req.CompanyId).Select(c => (int?)c.MoleculeId).FirstOrDefaultAsync();
                if (moleculeId.HasValue)
                {
                    await _hub.Clients.All.SendAsync("ShiftsUpdated", new { moleculeId = moleculeId.Value });
                }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "SignalR broadcast failed (non-fatal)"); }
        }
    }

    private static List<(DateOnly WorkDate, string ShiftTypeKey)> ComputeDesiredRows(TimeOffRequest req)
    {
        var rows = new List<(DateOnly, string)>();
        if (req.Status != RequestStatus.Approved) return rows;

        if (req.Type == TimeOffType.After)
        {
            rows.Add((req.StartDate, ShiftType.KEY_HOME_PM));
            rows.Add((req.StartDate.AddDays(1), ShiftType.KEY_HOME_AM));
        }
        else if (req.Type == TimeOffType.Vacation || req.Type == TimeOffType.DayAt) // DayAt materialises HOME like Vacation (Issue 4)
        {
            for (var d = req.StartDate; d <= req.EndDate; d = d.AddDays(1))
                rows.Add((d, ShiftType.KEY_HOME));
            rows.Add((req.EndDate.AddDays(1), ShiftType.KEY_HOME_AM));
        }
        return rows;
    }

    private async Task<int> EnsureInstanceAsync(int userId, DateOnly workDate, string shiftTypeKey)
    {
        // Resolve the user's molecule from their company
        var moleculeId = await _db.Users.IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .Join(_db.Companies.IgnoreQueryFilters(),
                  u => u.CompanyId, c => c.Id, (u, c) => c.MoleculeId)
            .FirstOrDefaultAsync();

        // The HomeType ShiftType for this molecule+key. Pre-seeded per Task 7 by HomeTypeService;
        // also fall back to creating one here if not present (defensive).
        var st = await _db.ShiftTypes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.MoleculeId == moleculeId && s.Key == shiftTypeKey);
        if (st == null)
        {
            // Defensive: create the ShiftType if missing
            (TimeOnly start, TimeOnly end, string nameEn, string nameHe) =
                shiftTypeKey == ShiftType.KEY_HOME ? (new TimeOnly(0, 0), new TimeOnly(23, 59), "Home", "בית")
                : shiftTypeKey == ShiftType.KEY_HOME_PM ? (new TimeOnly(16, 0), new TimeOnly(23, 59), "After", "אפטר")
                : (new TimeOnly(0, 0), new TimeOnly(13, 0), "After", "אפטר");
            st = new ShiftType
            {
                Key = shiftTypeKey,
                MoleculeId = moleculeId,
                Scope = ShiftScope.Molecule,
                Start = start,
                End = end,
                RowColor = "#F8E7B1",
                NameEn = nameEn,
                NameHe = nameHe
            };
            _db.ShiftTypes.Add(st);
            await _db.SaveChangesAsync();
        }

        var existing = await _db.ShiftInstances.IgnoreQueryFilters()
            .FirstOrDefaultAsync(si => si.WorkDate == workDate && si.ShiftTypeId == st.Id);
        if (existing != null) return existing.Id;

        var companyId = await _db.Users.IgnoreQueryFilters()
            .Where(u => u.Id == userId).Select(u => u.CompanyId).FirstOrDefaultAsync();
        var inst = new ShiftInstance
        {
            CompanyId = companyId,
            WorkDate = workDate,
            ShiftTypeId = st.Id,
            StaffingRequired = 99,
            UpdatedAt = DateTime.UtcNow
        };
        _db.ShiftInstances.Add(inst);
        await _db.SaveChangesAsync();
        return inst.Id;
    }

    public async Task RestoreRotationHomeAsync(int userId, DateOnly start, DateOnly end)
    {
        var user = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user?.HomeTypeId == null) return;

        await _homeTypeService.GenerateHomeShiftsAsync(
            user.HomeTypeId.Value, start, end, new List<int> { userId },
            createdByUserId: 0,
            mode: RegenerationMode.KeepManualChanges);
    }
}
