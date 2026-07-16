using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Hubs;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

// SECURITY-AUDITED: IgnoreQueryFilters() here is SAFE — draft sandboxes are molecule-scoped and keyed by
// explicit OwnerUserId; the Chores page gates ENTRY by the AssignChores grant, and COMMIT re-authorizes every
// touched cell (AuthorizeCellAsync — AssignChores for the cell user's company) before any live write, inside
// the lifecycle's transaction. This class is BOTH the chores staging/overlay surface (IDraftChoreService) and
// the chores commit reconciler (IDraftReconciler) plugged into the shared IDraftLifecycle (Spec C / F §4).
//
// A chore cell holds 0..N chores, so the payload is a canonical SET of rich descriptors (DraftChoreDescriptor)
// — NOT the int-CSV shifts use. Equality stays a plain string compare, preserving F's conflict primitive.
public class DraftChoreService : IDraftChoreService, IDraftReconciler
{
    private readonly AppDbContext _db;
    private readonly IBusyService _busyService;
    private readonly IGrantService _grantService;
    private readonly INotificationService _notificationService;
    private readonly IHubContext<CalendarHub>? _hub;

    // Post-commit side effects accumulated while ReconcileCellAsync runs (per applied cell), flushed once in
    // BroadcastAsync — chore create/cancel notifications fire in the live API pages (QuickAddChore /
    // DeleteChore), NOT the service, so commit must fire them itself. Firing AFTER the tx commits keeps the
    // (synchronous, un-rollbackable) email send out of the transaction and the created chores' Ids populated.
    private readonly List<Chore> _pendingAssigned = new();
    private readonly List<(int UserId, string Title, DateOnly Date, int ChoreId)> _pendingCanceled = new();

    public DraftChoreService(
        AppDbContext db,
        IBusyService busyService,
        IGrantService grantService,
        INotificationService notificationService,
        IHubContext<CalendarHub>? hub = null)
    {
        _db = db;
        _busyService = busyService;
        _grantService = grantService;
        _notificationService = notificationService;
        _hub = hub;
    }

    // ===================== IDraftChoreService — chores staging + overlay =====================

    public Task<DraftSession?> GetActiveChoreDraftAsync(int ownerUserId, int moleculeId, DateOnly weekStart)
        => _db.DraftSessions.FirstOrDefaultAsync(d =>
            d.OwnerUserId == ownerUserId && d.Surface == DraftSurface.Chores
            && d.MoleculeId == moleculeId && d.WeekStart == weekStart
            && d.Status == DraftSessionStatus.Active);

    // Live descriptor set for a (user, date) cell → canonical string (analog of GetLiveCellUsersAsync + Encode).
    private async Task<string> GetLiveCellCanonicalAsync(int userId, DateOnly date)
    {
        var chores = await _db.Chores.IgnoreQueryFilters()
            .Where(c => c.UserId == userId && c.Date == date && c.CanceledAt == null)
            .ToListAsync();
        return DraftChoreDescriptor.EncodeSet(chores.Select(DraftChoreDescriptor.FromChore));
    }

    private async Task<DraftChoreCell> GetOrCreateCellAsync(int draftSessionId, int userId, DateOnly date)
    {
        var cell = await _db.DraftChoreCells.FirstOrDefaultAsync(c =>
            c.DraftSessionId == draftSessionId && c.UserId == userId && c.WorkDate == date);
        if (cell != null) return cell;

        var baseline = await GetLiveCellCanonicalAsync(userId, date);
        cell = new DraftChoreCell
        {
            DraftSessionId = draftSessionId,
            UserId = userId,
            WorkDate = date,
            BaselineChores = baseline,
            StagedChores = baseline
        };
        _db.DraftChoreCells.Add(cell);
        return cell;
    }

    public async Task<bool> StageChoreAsync(int draftSessionId, int userId, DateOnly date, DraftChoreDescriptor descriptor)
    {
        if (!await IsActiveAsync(draftSessionId)) return false;
        var cell = await GetOrCreateCellAsync(draftSessionId, userId, date);
        var staged = DraftChoreDescriptor.DecodeSet(cell.StagedChores);
        staged.Add(DraftChoreDescriptor.Create(descriptor.ChoreTypeId, descriptor.Title, descriptor.StartTime, descriptor.EndTime, descriptor.Notes));
        cell.StagedChores = DraftChoreDescriptor.EncodeSet(staged); // EncodeSet de-dups, so re-adding an identical descriptor is a no-op
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ClearChoreAsync(int draftSessionId, int userId, DateOnly date, string descriptorKey)
    {
        if (!await IsActiveAsync(draftSessionId)) return false;
        var cell = await GetOrCreateCellAsync(draftSessionId, userId, date);
        var staged = DraftChoreDescriptor.DecodeSet(cell.StagedChores);
        staged.RemoveAll(d => string.Equals(d.Encode(), descriptorKey, StringComparison.Ordinal));
        cell.StagedChores = DraftChoreDescriptor.EncodeSet(staged);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<DraftChoreOverlayCell>> GetChoreOverlayAsync(int draftSessionId)
        => (await _db.DraftChoreCells.Where(c => c.DraftSessionId == draftSessionId).ToListAsync())
            .Select(c => new DraftChoreOverlayCell(c.UserId, c.WorkDate, DraftChoreDescriptor.DecodeSet(c.StagedChores)))
            .ToList();

    private Task<bool> IsActiveAsync(int draftSessionId)
        => _db.DraftSessions.AnyAsync(d => d.Id == draftSessionId && d.Status == DraftSessionStatus.Active);

    // ===================== IDraftReconciler — chores commit reconciler =====================

    public DraftSurface Surface => DraftSurface.Chores;

    public Task<string> CaptureBaselineAsync(DraftScope scope, CellKey cell)
        => GetLiveCellCanonicalAsync(cell.RowId, cell.Date);

    public async Task<IReadOnlyList<DraftCellState>> LoadTouchedCellsAsync(int draftSessionId)
        => (await _db.DraftChoreCells.Where(c => c.DraftSessionId == draftSessionId).ToListAsync())
            .Select(c => new DraftCellState(new CellKey(c.UserId, c.WorkDate), c.BaselineChores, c.StagedChores))
            .ToList();

    public Task<string> ReadLiveAsync(DraftScope scope, CellKey cell)
        => GetLiveCellCanonicalAsync(cell.RowId, cell.Date);

    public async Task<bool> AuthorizeCellAsync(int actingUserId, DraftScope scope, CellKey cell)
    {
        // cell.RowId = the chore recipient's user id. Re-check AssignChores for that user's company (mirrors
        // the live gate ChoreService.CanUserManageChoreForAssigneeAsync). SECURITY-AUDITED: SAFE — the user is
        // loaded by explicit id to derive the company for the grant scope check; grant is the boundary.
        var assignee = await _db.Users.IgnoreQueryFilters()
            .Where(u => u.Id == cell.RowId)
            .Select(u => new { u.CompanyId })
            .FirstOrDefaultAsync();
        if (assignee == null) return false;

        if (await _grantService.HasGrantAsync(actingUserId, "AdminAccess")) return true;

        return await _grantService.HasGrantForCompanyAsync(actingUserId, "AssignChores", assignee.CompanyId);
    }

    public async Task<IReadOnlyList<DraftValidationIssue>> ReconcileCellAsync(int actingUserId, DraftScope scope, DraftCellState cell)
    {
        var userId = cell.Cell.RowId;
        var date = cell.Cell.Date;
        var issues = new List<DraftValidationIssue>();

        // Set semantics keyed by the canonical per-descriptor string. Duplicate live chores that share a key
        // are treated as one logical descriptor (fungible — Spec C §6): a preserved key leaves ALL its live
        // rows untouched; a removed key cancels ALL its live rows; an added key creates ONE chore.
        var stagedKeys = DraftChoreDescriptor.DecodeSet(cell.Staged)
            .ToDictionary(d => d.Encode(), d => d, StringComparer.Ordinal);

        var liveChores = await _db.Chores.IgnoreQueryFilters()
            .Where(c => c.UserId == userId && c.Date == date && c.CanceledAt == null)
            .ToListAsync();
        var liveKeys = liveChores
            .GroupBy(c => DraftChoreDescriptor.FromChore(c).Encode(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        // REMOVE: each live descriptor not staged → soft-delete (CanceledAt/CanceledBy), NOT a hard delete.
        foreach (var (key, chores) in liveKeys)
        {
            if (stagedKeys.ContainsKey(key)) continue; // preserved — leave existing rows untouched (keeps Id/weight)
            foreach (var chore in chores)
            {
                chore.CanceledAt = DateTime.UtcNow;
                chore.CanceledBy = actingUserId;
                _pendingCanceled.Add((chore.UserId, chore.Title, chore.Date, chore.Id));
            }
        }

        // Additions need the (cell-constant) assignee + effective molecule. Resolve once — skip the whole ADD
        // pass if either is unresolvable (report one issue per staged descriptor that would have been created).
        var toAdd = stagedKeys.Where(kv => !liveKeys.ContainsKey(kv.Key)).Select(kv => kv.Value).ToList();
        if (toAdd.Count > 0)
        {
            var assignee = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
            // Effective molecule: the draft's molecule (chores are molecule-scoped); fall back to the assignee's
            // company molecule so a fresh chore is never left molecule-less.
            var effectiveMoleculeId = assignee == null
                ? null
                : scope.MoleculeId
                  ?? await _db.Companies.IgnoreQueryFilters()
                        .Where(co => co.Id == assignee.CompanyId).Select(co => co.MoleculeId).FirstOrDefaultAsync();

            foreach (var desc in toAdd)
            {
                if (assignee == null)
                {
                    issues.Add(new DraftValidationIssue(userId, date, userId, "assignee not found"));
                    continue;
                }
                if (!effectiveMoleculeId.HasValue)
                {
                    issues.Add(new DraftValidationIssue(userId, date, userId, "unable to resolve molecule"));
                    continue;
                }

                await AddStagedChoreAsync(actingUserId, userId, date, desc, assignee.CompanyId, effectiveMoleculeId.Value, issues);
            }
        }

        return issues;
    }

    // ADD one staged descriptor as a fresh live chore (inline — NOT ChoreService.CreateChoreAsync, which opens
    // its OWN transaction and would throw inside the lifecycle's ambient tx). Mirrors CreateChoreAsync's core:
    // re-validate via BusyService, freeze the fairness weight, insert with an explicit CompanyId.
    private async Task AddStagedChoreAsync(
        int actingUserId, int userId, DateOnly date, DraftChoreDescriptor desc,
        int companyId, int moleculeId, List<DraftValidationIssue> issues)
    {
        // Re-validate exactly like the live create. Hard errors (e.g. the user now has a live shift that
        // day) SKIP just this descriptor + report; warnings are the assigner's committed intent → proceed.
        var target = new BusyTarget.Chore(date, moleculeId, desc.ChoreTypeId);
        var validation = await _busyService.ValidateAsync(target, userId, actingUserId, overrideToken: null);
        if (!validation.CanProceed)
        {
            var firstErr = validation.Errors.FirstOrDefault();
            issues.Add(new DraftValidationIssue(userId, date, userId, firstErr?.Message ?? firstErr?.Key ?? "validation failed"));
            return;
        }

        int? typeDefaultWeight = null;
        if (desc.ChoreTypeId.HasValue)
        {
            typeDefaultWeight = await _db.ChoreTypes.IgnoreQueryFilters()
                .Where(ct => ct.Id == desc.ChoreTypeId.Value)
                .Select(ct => ct.DefaultWeightMinutes)
                .FirstOrDefaultAsync();
        }
        var weightMinutes = ChoreService.ResolveWeightMinutes(desc.StartTime, desc.EndTime, typeDefaultWeight);

        var chore = new Chore
        {
            CompanyId = companyId,      // Risk #7: stamp CompanyId explicitly (interceptor won't override)
            MoleculeId = moleculeId,
            ChoreTypeId = desc.ChoreTypeId,
            UserId = userId,
            Date = date,
            Title = desc.Title,
            Notes = desc.Notes,
            StartTime = desc.StartTime,
            EndTime = desc.EndTime,
            WeightMinutes = weightMinutes,
            CreatedBy = actingUserId,
            CreatedAt = DateTime.UtcNow
        };
        _db.Chores.Add(chore);
        _pendingAssigned.Add(chore); // Id populated by the lifecycle's SaveChanges before BroadcastAsync runs
    }

    public IEnumerable<string> NotifyGroupsFor(DraftScope scope, CellKey cell)
    {
        // Chores broadcast on the molecule group; there is NO jobType axis.
        yield return CalendarGroups.Chores(scope.MoleculeId ?? 0);
    }

    public async Task BroadcastAsync(IEnumerable<string> groups)
    {
        // (1) Per-user chore notifications (in-app + email) — parity with the live API pages. Post-commit +
        // best-effort: each is isolated so one bad recipient can't block the rest, and the whole call is
        // additionally wrapped by the lifecycle's try/catch (a failed notify must never fail an applied commit).
        foreach (var c in _pendingAssigned)
        {
            try { await _notificationService.CreateChoreAssignedNotificationAsync(c.UserId, c.Title, c.Date, c.Id); }
            catch { /* best-effort */ }
        }
        foreach (var (uid, title, date, choreId) in _pendingCanceled)
        {
            try { await _notificationService.CreateChoreCanceledNotificationAsync(uid, title, date, choreId); }
            catch { /* best-effort */ }
        }

        // (2) One lightweight "reload" signal per distinct group (clients shadow-refresh on ChoreChanged).
        if (_hub != null)
        {
            foreach (var group in groups.Distinct())
            {
                await _hub.Clients.Group(group).SendAsync("ChoreChanged",
                    new { changeType = "DraftCommitted", surface = nameof(DraftSurface.Chores) });
            }
        }
    }
}
