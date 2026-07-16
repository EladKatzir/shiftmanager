using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Hubs;
using ShiftManager.Models;

namespace ShiftManager.Services;

// SECURITY-AUDITED: IgnoreQueryFilters() here is SAFE — draft sandboxes are molecule-scoped and keyed by
// explicit OwnerUserId; callers gate ENTRY by shift-assignment grant, and COMMIT re-authorizes every cell
// (AuthorizeCellAsync — AssignShifts scoped to the cell's company+jobType) before any live write, inside the
// lifecycle's transaction. This class is BOTH the shifts staging/overlay surface (IDraftModeService) and the
// shifts commit reconciler (IDraftReconciler) plugged into the shared IDraftLifecycle.
public class DraftModeService : IDraftModeService, IDraftReconciler
{
    private readonly AppDbContext _db;
    private readonly IShiftAssignmentService _assignmentService;
    private readonly IGrantService _grantService;
    private readonly IHubContext<CalendarHub>? _hub;

    public DraftModeService(
        AppDbContext db,
        IShiftAssignmentService assignmentService,
        IGrantService grantService,
        IHubContext<CalendarHub>? hub = null)
    {
        _db = db;
        _assignmentService = assignmentService;
        _grantService = grantService;
        _hub = hub;
    }

    // ===================== IDraftModeService — shifts staging + overlay =====================

    public Task<DraftSession?> GetActiveDraftAsync(int ownerUserId, int moleculeId, int? jobTypeId, DateOnly weekStart)
        => _db.DraftSessions.FirstOrDefaultAsync(d =>
            d.OwnerUserId == ownerUserId && d.Surface == DraftSurface.Shifts
            && d.MoleculeId == moleculeId && d.JobTypeId == jobTypeId
            && d.WeekStart == weekStart && d.Status == DraftSessionStatus.Active);

    private Task<List<int>> GetLiveCellUsersAsync(int shiftTypeId, DateOnly date)
        => _db.ShiftAssignments.IgnoreQueryFilters()
            .Where(a => a.UserId != null && a.ShiftInstance.ShiftTypeId == shiftTypeId && a.ShiftInstance.WorkDate == date)
            .Select(a => a.UserId!.Value)
            .ToListAsync();

    private async Task<DraftCell> GetOrCreateCellAsync(int draftSessionId, int shiftTypeId, DateOnly date)
    {
        var cell = await _db.DraftCells.FirstOrDefaultAsync(c =>
            c.DraftSessionId == draftSessionId && c.ShiftTypeId == shiftTypeId && c.WorkDate == date);
        if (cell != null) return cell;

        var baseline = DraftCell.Encode(await GetLiveCellUsersAsync(shiftTypeId, date));
        cell = new DraftCell
        {
            DraftSessionId = draftSessionId,
            ShiftTypeId = shiftTypeId,
            WorkDate = date,
            BaselineUserIds = baseline,
            StagedUserIds = baseline
        };
        _db.DraftCells.Add(cell);
        return cell;
    }

    public async Task<bool> StageAssignAsync(int draftSessionId, int shiftTypeId, DateOnly date, int userId)
    {
        if (!await IsActiveAsync(draftSessionId)) return false;
        var cell = await GetOrCreateCellAsync(draftSessionId, shiftTypeId, date);
        var staged = DraftCell.Decode(cell.StagedUserIds);
        if (!staged.Contains(userId)) staged.Add(userId);
        cell.StagedUserIds = DraftCell.Encode(staged);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> StageClearAsync(int draftSessionId, int shiftTypeId, DateOnly date, int userId)
    {
        if (!await IsActiveAsync(draftSessionId)) return false;
        var cell = await GetOrCreateCellAsync(draftSessionId, shiftTypeId, date);
        var staged = DraftCell.Decode(cell.StagedUserIds);
        staged.Remove(userId);
        cell.StagedUserIds = DraftCell.Encode(staged);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<DraftOverlayCell>> GetOverlayAsync(int draftSessionId)
        => (await _db.DraftCells.Where(c => c.DraftSessionId == draftSessionId).ToListAsync())
            .Select(c => new DraftOverlayCell(c.ShiftTypeId, c.WorkDate, DraftCell.Decode(c.StagedUserIds)))
            .ToList();

    private Task<bool> IsActiveAsync(int draftSessionId)
        => _db.DraftSessions.AnyAsync(d => d.Id == draftSessionId && d.Status == DraftSessionStatus.Active);

    // ===================== IDraftReconciler — shifts commit reconciler =====================

    public DraftSurface Surface => DraftSurface.Shifts;

    public async Task<string> CaptureBaselineAsync(DraftScope scope, CellKey cell)
        => DraftCell.Encode(await GetLiveCellUsersAsync(cell.RowId, cell.Date));

    public async Task<IReadOnlyList<DraftCellState>> LoadTouchedCellsAsync(int draftSessionId)
        => (await _db.DraftCells.Where(c => c.DraftSessionId == draftSessionId).ToListAsync())
            // Foundation canonical = primaries only. Sub-project A folds trainees into this string so a
            // trainee-only change also registers as "changed" (its columns already exist on DraftCell).
            .Select(c => new DraftCellState(new CellKey(c.ShiftTypeId, c.WorkDate), c.BaselineUserIds, c.StagedUserIds))
            .ToList();

    public Task<string> ReadLiveAsync(DraftScope scope, CellKey cell)
        => CaptureBaselineAsync(scope, cell);

    public async Task<bool> AuthorizeCellAsync(int actingUserId, DraftScope scope, CellKey cell)
    {
        // SECURITY-AUDITED: SAFE — shift types are not tenant-filtered; loaded by explicit id to derive the
        // cell's company+jobType for the scope check. Mirrors the live assign gate (Table.cshtml.cs:815-824).
        var st = await _db.ShiftTypes.IgnoreQueryFilters()
            .Where(s => s.Id == cell.RowId)
            .Select(s => new { s.JobTypeId, s.CompanyId })
            .FirstOrDefaultAsync();
        if (st == null) return false;

        if (await _grantService.HasGrantAsync(actingUserId, "AdminAccess")) return true;

        // Cell company: the shift type's own company, or the acting user's company for a molecule-scoped type
        // (the same effective company the reconcile stamps onto a newly created instance).
        var actingCompanyId = await _db.Users.IgnoreQueryFilters()
            .Where(u => u.Id == actingUserId).Select(u => u.CompanyId).FirstOrDefaultAsync();
        var cellCompanyId = st.CompanyId ?? actingCompanyId;

        return await _grantService.HasGrantWithScopeAsync(
            actingUserId, "AssignShifts", companyId: cellCompanyId, jobTypeId: st.JobTypeId);
    }

    public async Task<IReadOnlyList<DraftValidationIssue>> ReconcileCellAsync(int actingUserId, DraftScope scope, DraftCellState cell)
    {
        var shiftTypeId = cell.Cell.RowId;
        var date = cell.Cell.Date;
        var staged = DraftCell.Decode(cell.Staged).ToHashSet();
        var issues = new List<DraftValidationIssue>();

        // Fallback company for a molecule-scoped shift type that needs a fresh ShiftInstance.
        var actingCompanyId = await _db.Users.IgnoreQueryFilters()
            .Where(u => u.Id == actingUserId).Select(u => u.CompanyId).FirstOrDefaultAsync();

        var instance = await _db.ShiftInstances.IgnoreQueryFilters()
            .FirstOrDefaultAsync(si => si.ShiftTypeId == shiftTypeId && si.WorkDate == date);
        if (instance == null)
        {
            var st = await _db.ShiftTypes.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == shiftTypeId);
            if (st == null) return issues;
            instance = new ShiftInstance
            {
                CompanyId = st.GetEffectiveCompanyId(actingCompanyId),
                ShiftTypeId = shiftTypeId,
                WorkDate = date,
                StaffingRequired = Math.Max(1, staged.Count)
            };
            _db.ShiftInstances.Add(instance);
            await _db.SaveChangesAsync();
        }

        var slots = await _db.ShiftAssignments.IgnoreQueryFilters()
            .Where(a => a.ShiftInstanceId == instance.Id).ToListAsync();
        var currentlyAssigned = slots.Where(s => s.UserId != null).ToList();
        var currentUserIds = currentlyAssigned.Select(s => s.UserId!.Value).ToHashSet();

        // Remove users no longer staged.
        foreach (var slot in currentlyAssigned.Where(s => !staged.Contains(s.UserId!.Value)))
        {
            slot.UserId = null;
            slot.TraineeUserId = null;
        }

        // Add staged users not currently assigned (final validation pass; a hard error skips just that user).
        foreach (var userId in staged.Where(uid => !currentUserIds.Contains(uid)))
        {
            var validation = await _assignmentService.ValidateShiftAssignmentAsync(userId, instance.Id);
            if (!validation.CanAssign)
            {
                issues.Add(new DraftValidationIssue(shiftTypeId, date, userId,
                    validation.Errors.FirstOrDefault()?.Message ?? "validation failed"));
                continue;
            }

            var emptySlot = slots.FirstOrDefault(s => s.UserId == null);
            if (emptySlot != null)
            {
                emptySlot.UserId = userId;
            }
            else
            {
                var newSlot = new ShiftAssignment { CompanyId = instance.CompanyId, ShiftInstanceId = instance.Id, UserId = userId };
                _db.ShiftAssignments.Add(newSlot);
                slots.Add(newSlot);
            }
        }

        return issues;
    }

    public IEnumerable<string> NotifyGroupsFor(DraftScope scope, CellKey cell)
    {
        // The draft is entered per (molecule, jobType) board, so every cell notifies that board's group.
        yield return CalendarGroups.Shifts(scope.MoleculeId ?? 0, scope.JobTypeId);
    }

    public async Task BroadcastAsync(IEnumerable<string> groups)
    {
        if (_hub == null) return;
        // Clients shadow-refresh the grid on AssignmentChanged (payload is only logged), so one lightweight
        // "reload" signal per group is the right batch notification for a commit.
        foreach (var group in groups.Distinct())
        {
            await _hub.Clients.Group(group).SendAsync("AssignmentChanged",
                new { changeType = "DraftCommitted", surface = nameof(DraftSurface.Shifts) });
        }
    }
}
