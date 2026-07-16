using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Hubs;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

// SECURITY-AUDITED: IgnoreQueryFilters() here is SAFE and, for OnDuty, a no-op — OnDuty is a GLOBAL table
// with no query filter (cross-company by design). Draft sandboxes are keyed by explicit OwnerUserId; callers
// gate ENTRY by the on-call OR-chain grant, and COMMIT re-authorizes every cell (AuthorizeCellAsync →
// CanUserManageOnDutyAsync) before any live write, inside the shared lifecycle's transaction. This class is
// BOTH the on-call staging/overlay surface (IDraftDutyService) and the on-call commit reconciler
// (IDraftReconciler) plugged into the shared IDraftLifecycle.
//
// SCOPE LANDMINE (Spec D §3): baseline / staged-target / live-now are computed over the GLOBAL active OnDuty
// set for (dutyTypeValue, date). The area filter is NEVER applied here — it is a UI convenience only. Two
// owners "drafting different areas" for the same (type, date) edit the SAME global cell and legitimately
// conflict at commit; that is correct, not a bug.
public class DraftDutyService : IDraftDutyService, IDraftReconciler
{
    private readonly AppDbContext _db;
    private readonly IOnDutyService _onDutyService;

    public DraftDutyService(AppDbContext db, IOnDutyService onDutyService)
    {
        _db = db;
        _onDutyService = onDutyService;
    }

    // ===================== IDraftDutyService — on-call staging + overlay =====================

    public Task<DraftSession?> GetActiveDraftAsync(int ownerUserId, int? areaId, DateOnly weekStart)
        => _db.DraftSessions.FirstOrDefaultAsync(d =>
            d.OwnerUserId == ownerUserId && d.Surface == DraftSurface.OnCall
            && d.AreaId == areaId
            && d.WeekStart == weekStart && d.Status == DraftSessionStatus.Active);

    /// <summary>
    /// GLOBAL live user set for (dutyTypeValue, date) — the active (non-canceled) OnDuty rows. IgnoreQueryFilters
    /// is a documented no-op (OnDuty is un-filtered); the area filter is deliberately NOT applied (Spec D §3).
    /// </summary>
    private Task<List<int>> GetLiveCellUsersAsync(int dutyTypeValue, DateOnly date)
    {
        var type = (OnDutyType)dutyTypeValue;
        return _db.OnDuties.IgnoreQueryFilters()
            .Where(o => o.Type == type && o.Date == date && o.CanceledAt == null)
            .Select(o => o.UserId)
            .ToListAsync();
    }

    private async Task<DraftDutyCell> GetOrCreateCellAsync(int draftSessionId, int dutyTypeValue, DateOnly date)
    {
        var cell = await _db.DraftDutyCells.FirstOrDefaultAsync(c =>
            c.DraftSessionId == draftSessionId && c.DutyTypeValue == dutyTypeValue && c.WorkDate == date);
        if (cell != null) return cell;

        var baseline = DraftCell.Encode(await GetLiveCellUsersAsync(dutyTypeValue, date));
        cell = new DraftDutyCell
        {
            DraftSessionId = draftSessionId,
            DutyTypeValue = dutyTypeValue,
            WorkDate = date,
            BaselineUserIds = baseline,
            StagedUserIds = baseline
        };
        _db.DraftDutyCells.Add(cell);
        return cell;
    }

    public async Task<bool> StageDutyAssignAsync(int draftSessionId, int dutyTypeValue, DateOnly date, int userId)
    {
        if (!await IsActiveAsync(draftSessionId)) return false;
        var cell = await GetOrCreateCellAsync(draftSessionId, dutyTypeValue, date);
        var staged = DraftCell.Decode(cell.StagedUserIds);
        if (!staged.Contains(userId)) staged.Add(userId);
        cell.StagedUserIds = DraftCell.Encode(staged);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> StageDutyClearAsync(int draftSessionId, int dutyTypeValue, DateOnly date, int userId)
    {
        if (!await IsActiveAsync(draftSessionId)) return false;
        var cell = await GetOrCreateCellAsync(draftSessionId, dutyTypeValue, date);
        var staged = DraftCell.Decode(cell.StagedUserIds);
        staged.Remove(userId);
        cell.StagedUserIds = DraftCell.Encode(staged);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<DraftDutyOverlayCell>> GetDutyOverlayAsync(int draftSessionId)
        => (await _db.DraftDutyCells.Where(c => c.DraftSessionId == draftSessionId).ToListAsync())
            .Select(c => new DraftDutyOverlayCell(c.DutyTypeValue, c.WorkDate, DraftCell.Decode(c.StagedUserIds)))
            .ToList();

    private Task<bool> IsActiveAsync(int draftSessionId)
        => _db.DraftSessions.AnyAsync(d => d.Id == draftSessionId && d.Status == DraftSessionStatus.Active);

    // ===================== IDraftReconciler — on-call commit reconciler =====================

    public DraftSurface Surface => DraftSurface.OnCall;

    public async Task<string> CaptureBaselineAsync(DraftScope scope, CellKey cell)
        => DraftCell.Encode(await GetLiveCellUsersAsync(cell.RowId, cell.Date));

    public async Task<IReadOnlyList<DraftCellState>> LoadTouchedCellsAsync(int draftSessionId)
        => (await _db.DraftDutyCells.Where(c => c.DraftSessionId == draftSessionId).ToListAsync())
            .Select(c => new DraftCellState(new CellKey(c.DutyTypeValue, c.WorkDate), c.BaselineUserIds, c.StagedUserIds))
            .ToList();

    public Task<string> ReadLiveAsync(DraftScope scope, CellKey cell)
        => CaptureBaselineAsync(scope, cell);

    /// <summary>
    /// Commit-time re-authorization (Spec D §5, F §5b/§7). Each on-call cell IS a duty type (RowId =
    /// DutyTypeValue), so re-checking per cell IS re-checking per duty type. Parity with the live edit gate:
    /// the OR-chain <c>ManageOnDuty || AssignHakamDuties || AssignKatzinDuties || EditOnCallCalendar</c> is
    /// undifferentiated by duty type there too, so we call the same authority.
    /// </summary>
    public Task<bool> AuthorizeCellAsync(int actingUserId, DraftScope scope, CellKey cell)
        => _onDutyService.CanUserManageOnDutyAsync(actingUserId);

    public async Task<IReadOnlyList<DraftValidationIssue>> ReconcileCellAsync(int actingUserId, DraftScope scope, DraftCellState cell)
    {
        var dutyTypeValue = cell.Cell.RowId;
        var date = cell.Cell.Date;
        var type = (OnDutyType)dutyTypeValue;
        var staged = DraftCell.Decode(cell.Staged).ToHashSet();
        var issues = new List<DraftValidationIssue>();

        // Current live set == baseline here (the lifecycle already drift-checked this cell inside the tx).
        var live = (await GetLiveCellUsersAsync(dutyTypeValue, date)).ToHashSet();

        // staged − live → create a global OnDuty row per newly-staged user. A hard error (rank-ineligible,
        // busy hard-block) OR an un-forced warning (BUSY_OVERRIDE_REQUIRED) skips JUST that user + reports it
        // (auto-skip-and-report — commit can't prompt for an override). forceAssign stays false by design.
        foreach (var userId in staged.Where(uid => !live.Contains(uid)))
        {
            var (success, message, _, _, _) =
                await _onDutyService.CreateForActorAsync(actingUserId, userId, date, type, notes: null, forceAssign: false);
            if (!success)
                issues.Add(new DraftValidationIssue(dutyTypeValue, date, userId, message));
        }

        // live − staged → soft-delete the active OnDuty row(s) for (user, type, date). No empty-slot reuse
        // (on-call has no fixed slots — one row per assignment).
        foreach (var userId in live.Where(uid => !staged.Contains(uid)))
        {
            var rows = await _db.OnDuties.IgnoreQueryFilters()
                .Where(o => o.UserId == userId && o.Type == type && o.Date == date && o.CanceledAt == null)
                .Select(o => o.Id)
                .ToListAsync();
            foreach (var onDutyId in rows)
            {
                var (success, message) = await _onDutyService.CancelForActorAsync(actingUserId, onDutyId);
                if (!success)
                    issues.Add(new DraftValidationIssue(dutyTypeValue, date, userId, message));
            }
        }

        return issues;
    }

    // SignalR PARITY (Spec D §5): the live on-call write path broadcasts nothing today
    // (NotifyOnCallChangedAsync is never called), so a draft commit broadcasts nothing either. To enable
    // real-time later, return CalendarGroups.OnCall(scope.AreaId ?? 0) here and send it in BroadcastAsync.
    public IEnumerable<string> NotifyGroupsFor(DraftScope scope, CellKey cell)
        => Array.Empty<string>();

    public Task BroadcastAsync(IEnumerable<string> groups) => Task.CompletedTask;
}
