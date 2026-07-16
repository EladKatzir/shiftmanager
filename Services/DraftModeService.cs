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

    // Live primary→trainee pairings for a cell (both non-null). Companion of GetLiveCellUsersAsync so the
    // baseline/drift string carries trainees too (sub-project A — a trainee-only change must register as
    // "changed", and a live trainee removed by another user is caught as drift).
    private async Task<Dictionary<int, int>> GetLiveCellTraineesAsync(int shiftTypeId, DateOnly date)
        => (await _db.ShiftAssignments.IgnoreQueryFilters()
                .Where(a => a.UserId != null && a.TraineeUserId != null
                            && a.ShiftInstance.ShiftTypeId == shiftTypeId && a.ShiftInstance.WorkDate == date)
                .Select(a => new { Primary = a.UserId!.Value, Trainee = a.TraineeUserId!.Value })
                .ToListAsync())
            // A primary occupies a single slot, so one trainee per primary; guard duplicates defensively.
            .GroupBy(x => x.Primary)
            .ToDictionary(g => g.Key, g => g.Last().Trainee);

    private async Task<DraftCell> GetOrCreateCellAsync(int draftSessionId, int shiftTypeId, DateOnly date)
    {
        var cell = await _db.DraftCells.FirstOrDefaultAsync(c =>
            c.DraftSessionId == draftSessionId && c.ShiftTypeId == shiftTypeId && c.WorkDate == date);
        if (cell != null) return cell;

        var baseline = DraftCell.Encode(await GetLiveCellUsersAsync(shiftTypeId, date));
        // Capture the trainee baseline on the SAME first touch so an untouched live trainee is mirrored into
        // Staged (preserved at commit) and any later live trainee change reads as drift.
        var traineeBaseline = DraftCell.EncodePairs(await GetLiveCellTraineesAsync(shiftTypeId, date));
        cell = new DraftCell
        {
            DraftSessionId = draftSessionId,
            ShiftTypeId = shiftTypeId,
            WorkDate = date,
            BaselineUserIds = baseline,
            StagedUserIds = baseline,
            BaselineTrainees = traineeBaseline,
            StagedTrainees = traineeBaseline
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
        // A trainee cannot outlive its primary (edge-case ruling 1): clearing the primary drops its staged trainee.
        var trainees = DraftCell.DecodePairs(cell.StagedTrainees);
        if (trainees.Remove(userId))
            cell.StagedTrainees = DraftCell.EncodePairs(trainees);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> StageTraineeAsync(int draftSessionId, int shiftTypeId, DateOnly date, int primaryUserId, int traineeUserId)
    {
        if (!await IsActiveAsync(draftSessionId)) return false;
        var cell = await GetOrCreateCellAsync(draftSessionId, shiftTypeId, date);
        var trainees = DraftCell.DecodePairs(cell.StagedTrainees);
        trainees[primaryUserId] = traineeUserId; // one trainee per primary — last write wins
        cell.StagedTrainees = DraftCell.EncodePairs(trainees);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> StageTraineeClearAsync(int draftSessionId, int shiftTypeId, DateOnly date, int primaryUserId)
    {
        if (!await IsActiveAsync(draftSessionId)) return false;
        var cell = await GetOrCreateCellAsync(draftSessionId, shiftTypeId, date);
        var trainees = DraftCell.DecodePairs(cell.StagedTrainees);
        if (trainees.Remove(primaryUserId))
        {
            cell.StagedTrainees = DraftCell.EncodePairs(trainees);
            await _db.SaveChangesAsync();
        }
        return true;
    }

    public async Task<List<DraftOverlayCell>> GetOverlayAsync(int draftSessionId)
        => (await _db.DraftCells.Where(c => c.DraftSessionId == draftSessionId).ToListAsync())
            .Select(c => new DraftOverlayCell(
                c.ShiftTypeId, c.WorkDate, DraftCell.Decode(c.StagedUserIds), DraftCell.DecodePairs(c.StagedTrainees)))
            .ToList();

    private Task<bool> IsActiveAsync(int draftSessionId)
        => _db.DraftSessions.AnyAsync(d => d.Id == draftSessionId && d.Status == DraftSessionStatus.Active);

    // ===================== IDraftReconciler — shifts commit reconciler =====================

    public DraftSurface Surface => DraftSurface.Shifts;

    // Canonical shift-cell serialization (sub-project A): primaries CSV, then trainees pair-CSV, joined by a
    // separator neither part can contain (ids are digits; pairs are "p:t"). Folding trainees in makes the
    // Foundation's plain string equality (no-op skip + drift check) trainee-aware for free.
    private const char CanonicalSeparator = '|';
    private static string Canonical(string primaries, string trainees)
        => string.Concat(primaries, CanonicalSeparator.ToString(), trainees);

    public async Task<string> CaptureBaselineAsync(DraftScope scope, CellKey cell)
        => Canonical(
            DraftCell.Encode(await GetLiveCellUsersAsync(cell.RowId, cell.Date)),
            DraftCell.EncodePairs(await GetLiveCellTraineesAsync(cell.RowId, cell.Date)));

    public async Task<IReadOnlyList<DraftCellState>> LoadTouchedCellsAsync(int draftSessionId)
        => (await _db.DraftCells.Where(c => c.DraftSessionId == draftSessionId).ToListAsync())
            // Fold trainees into the canonical string so a trainee-only change registers as "changed" (G1) and
            // trainee drift is caught by the Foundation per-cell drift check (edge-case ruling 4).
            .Select(c => new DraftCellState(new CellKey(c.ShiftTypeId, c.WorkDate),
                Canonical(c.BaselineUserIds, c.BaselineTrainees),
                Canonical(c.StagedUserIds, c.StagedTrainees)))
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
        // Canonical staged string = "primaries|trainees"; split once and decode each half.
        var parts = cell.Staged.Split(CanonicalSeparator, 2);
        var staged = DraftCell.Decode(parts[0]).ToHashSet();
        var stagedTrainees = DraftCell.DecodePairs(parts.Length > 1 ? parts[1] : string.Empty);
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
        else if (staged.Count > instance.StaffingRequired)
        {
            // Sub-project B (capacity): the staged assignee count IS the intended capacity. Widen an EXISTING
            // instance to fit at COMMIT — never live (staging never touched capacity). The reconcile already
            // sizes a freshly created instance the same way above.
            instance.StaffingRequired = staged.Count;
        }

        var slots = await _db.ShiftAssignments.IgnoreQueryFilters()
            .Where(a => a.ShiftInstanceId == instance.Id).ToListAsync();
        var currentlyAssigned = slots.Where(s => s.UserId != null).ToList();
        var currentUserIds = currentlyAssigned.Select(s => s.UserId!.Value).ToHashSet();

        // Remove users no longer staged (their trainee goes with them — trainees cannot outlive their primary).
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

        // Third pass (sub-project A) — trainee shadows, applied ONLY on primaries that assigned successfully
        // (G3/G5). A slot with a user reconciles its trainee to the staged desire: set the staged trainee
        // (validated), or clear when nothing is staged for that primary. StagedTrainees mirrors the baseline
        // for an untouched cell, so a live trainee the draft never touched is preserved here (F Risk #4).
        var assignedPrimaries = slots.Where(s => s.UserId != null).Select(s => s.UserId!.Value).ToHashSet();
        foreach (var slot in slots.Where(s => s.UserId != null))
        {
            var primary = slot.UserId!.Value;
            if (stagedTrainees.TryGetValue(primary, out var traineeId))
            {
                if (slot.TraineeUserId == traineeId) continue; // no-op (fast path: preserves untouched trainee)
                var tv = await _assignmentService.ValidateTraineeAssignmentAsync(traineeId, primary, instance.Id);
                if (!tv.CanAssign)
                {
                    issues.Add(new DraftValidationIssue(shiftTypeId, date, traineeId,
                        tv.Errors.FirstOrDefault()?.Message ?? "trainee validation failed"));
                    continue; // skip the trainee; the primary still applied
                }
                slot.TraineeUserId = traineeId;
            }
            else
            {
                slot.TraineeUserId = null; // staged intent: this primary shadows no trainee
            }
        }

        // Gate on primary success (G5): a staged trainee whose primary did NOT make it into a slot is skipped
        // and reported (e.g. the primary hard-errored above, or was never staged into this cell).
        foreach (var kv in stagedTrainees.Where(kv => !assignedPrimaries.Contains(kv.Key)))
        {
            issues.Add(new DraftValidationIssue(shiftTypeId, date, kv.Value,
                "trainee skipped — its primary was not assigned"));
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
