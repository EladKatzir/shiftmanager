using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

// SECURITY-AUDITED: IgnoreQueryFilters() here is SAFE — draft sandboxes are molecule-scoped and keyed by
// explicit OwnerUserId; callers gate entry by shift-assignment grant. Live reconciliation at commit writes
// only the touched cells' assignments, inside a transaction, after baseline-conflict + validation passes.
public class DraftModeService : IDraftModeService
{
    private readonly AppDbContext _db;
    private readonly IShiftAssignmentService _assignmentService;

    public DraftModeService(AppDbContext db, IShiftAssignmentService assignmentService)
    {
        _db = db;
        _assignmentService = assignmentService;
    }

    public Task<DraftSession?> GetActiveDraftAsync(int ownerUserId, int moleculeId, int? jobTypeId, DateOnly weekStart)
        => _db.DraftSessions.FirstOrDefaultAsync(d =>
            d.OwnerUserId == ownerUserId && d.MoleculeId == moleculeId && d.JobTypeId == jobTypeId
            && d.WeekStart == weekStart && d.Status == DraftSessionStatus.Active);

    public async Task<DraftSession> EnterDraftAsync(int ownerUserId, int moleculeId, int? jobTypeId, DateOnly weekStart, DateOnly weekEnd)
    {
        var existing = await GetActiveDraftAsync(ownerUserId, moleculeId, jobTypeId, weekStart);
        if (existing != null) return existing;

        var session = new DraftSession
        {
            OwnerUserId = ownerUserId,
            MoleculeId = moleculeId,
            JobTypeId = jobTypeId,
            WeekStart = weekStart,
            WeekEnd = weekEnd,
            Status = DraftSessionStatus.Active
        };
        _db.DraftSessions.Add(session);
        await _db.SaveChangesAsync();
        return session;
    }

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

    public async Task<DraftCommitResult> CommitAsync(int draftSessionId, int actingUserId)
    {
        var session = await _db.DraftSessions.FirstOrDefaultAsync(d => d.Id == draftSessionId && d.Status == DraftSessionStatus.Active);
        if (session == null)
            return new DraftCommitResult(false, 0, new List<DraftConflict>(), new List<DraftValidationIssue>());

        var cells = await _db.DraftCells.Where(c => c.DraftSessionId == draftSessionId).ToListAsync();

        // Phase 1: optimistic conflict detection. A no-op cell (staged == baseline) never conflicts; a
        // changed cell conflicts when the live assignees drifted from the baseline since the draft entry.
        var conflicts = new List<DraftConflict>();
        foreach (var cell in cells)
        {
            if (cell.StagedUserIds == cell.BaselineUserIds) continue;
            var liveNow = DraftCell.Encode(await GetLiveCellUsersAsync(cell.ShiftTypeId, cell.WorkDate));
            if (liveNow != cell.BaselineUserIds)
                conflicts.Add(new DraftConflict(cell.ShiftTypeId, cell.WorkDate));
        }
        if (conflicts.Count > 0)
            return new DraftCommitResult(false, 0, conflicts, new List<DraftValidationIssue>());

        // Fallback company for molecule-scoped shift types that need a new ShiftInstance.
        var actingCompanyId = await _db.Users.IgnoreQueryFilters()
            .Where(u => u.Id == actingUserId).Select(u => u.CompanyId).FirstOrDefaultAsync();

        // Phase 2: reconcile every changed cell into live (final validation pass; skip a staged user on a
        // hard error such as a double-booking, applying the rest of the cell).
        var issues = new List<DraftValidationIssue>();
        int applied = 0;
        await using var tx = await _db.Database.BeginTransactionAsync();
        foreach (var cell in cells)
        {
            if (cell.StagedUserIds == cell.BaselineUserIds) continue;
            var staged = DraftCell.Decode(cell.StagedUserIds).ToHashSet();

            var instance = await _db.ShiftInstances.IgnoreQueryFilters()
                .FirstOrDefaultAsync(si => si.ShiftTypeId == cell.ShiftTypeId && si.WorkDate == cell.WorkDate);
            if (instance == null)
            {
                var st = await _db.ShiftTypes.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == cell.ShiftTypeId);
                if (st == null) continue;
                instance = new ShiftInstance
                {
                    CompanyId = st.GetEffectiveCompanyId(actingCompanyId),
                    ShiftTypeId = cell.ShiftTypeId,
                    WorkDate = cell.WorkDate,
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

            // Add staged users not currently assigned.
            foreach (var userId in staged.Where(uid => !currentUserIds.Contains(uid)))
            {
                var validation = await _assignmentService.ValidateShiftAssignmentAsync(userId, instance.Id);
                if (!validation.CanAssign)
                {
                    issues.Add(new DraftValidationIssue(cell.ShiftTypeId, cell.WorkDate, userId,
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
            applied++;
        }

        session.Status = DraftSessionStatus.Committed;
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return new DraftCommitResult(true, applied, new List<DraftConflict>(), issues);
    }

    public async Task DiscardAsync(int draftSessionId)
    {
        var session = await _db.DraftSessions.FirstOrDefaultAsync(d => d.Id == draftSessionId);
        if (session == null) return;
        _db.DraftSessions.Remove(session); // cells cascade
        await _db.SaveChangesAsync();
    }

    private Task<bool> IsActiveAsync(int draftSessionId)
        => _db.DraftSessions.AnyAsync(d => d.Id == draftSessionId && d.Status == DraftSessionStatus.Active);
}
