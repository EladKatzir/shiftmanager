using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

// SECURITY-AUDITED: this orchestrator reads/writes draft sessions with NO tenant query filter — sandboxes
// are keyed by explicit OwnerUserId and scoped by molecule (shifts/chores) or global (on-call). Draft-entry
// grants are checked by the surface's page handler; commit RE-authorizes every touched cell per-surface
// (Spec F §5b, §7) through the reconciler before any live write, inside the transaction.
public class DraftLifecycle : IDraftLifecycle
{
    private readonly AppDbContext _db;
    private readonly IReadOnlyDictionary<DraftSurface, IDraftReconciler> _reconcilers;

    public DraftLifecycle(AppDbContext db, IEnumerable<IDraftReconciler> reconcilers)
    {
        _db = db;
        // One reconciler per surface. Duplicate registrations are a DI misconfiguration — fail loud.
        _reconcilers = reconcilers.ToDictionary(r => r.Surface);
    }

    private IDraftReconciler ReconcilerFor(DraftSurface surface) =>
        _reconcilers.TryGetValue(surface, out var r)
            ? r
            : throw new InvalidOperationException($"No IDraftReconciler is registered for draft surface {surface}.");

    private static DraftScope ScopeOf(DraftSession s) =>
        new(s.Surface, s.MoleculeId, s.JobTypeId, s.AreaId, s.WeekStart, s.WeekEnd);

    public Task<DraftSession?> GetActiveAsync(int ownerUserId, DraftScope scope)
        => _db.DraftSessions.FirstOrDefaultAsync(d =>
            d.OwnerUserId == ownerUserId
            && d.Surface == scope.Surface
            && d.MoleculeId == scope.MoleculeId
            && d.JobTypeId == scope.JobTypeId
            && d.AreaId == scope.AreaId
            && d.WeekStart == scope.WeekStart
            && d.Status == DraftSessionStatus.Active);

    public async Task<DraftSession> EnterAsync(int ownerUserId, DraftScope scope)
    {
        var existing = await GetActiveAsync(ownerUserId, scope);
        if (existing != null) return existing;

        var session = new DraftSession
        {
            OwnerUserId = ownerUserId,
            Surface = scope.Surface,
            MoleculeId = scope.MoleculeId,
            JobTypeId = scope.JobTypeId,
            AreaId = scope.AreaId,
            WeekStart = scope.WeekStart,
            WeekEnd = scope.WeekEnd,
            Status = DraftSessionStatus.Active
        };
        _db.DraftSessions.Add(session);
        await _db.SaveChangesAsync(); // filtered-unique index is the race safety-net for the single-active invariant
        return session;
    }

    public async Task DiscardAsync(int draftSessionId, int ownerUserId)
    {
        var session = await _db.DraftSessions
            .FirstOrDefaultAsync(d => d.Id == draftSessionId && d.OwnerUserId == ownerUserId);
        if (session == null) return;
        _db.DraftSessions.Remove(session); // cells cascade
        await _db.SaveChangesAsync();
    }

    public async Task<DraftCommitResult> CommitAsync(int draftSessionId, int actingUserId)
    {
        var empty = new DraftCommitResult(false,
            Array.Empty<CellRef>(), Array.Empty<CellRef>(), Array.Empty<CellRef>(),
            Array.Empty<DraftValidationIssue>(), Array.Empty<string>());

        var session = await _db.DraftSessions
            .FirstOrDefaultAsync(d => d.Id == draftSessionId && d.Status == DraftSessionStatus.Active);
        if (session == null) return empty;

        var reconciler = ReconcilerFor(session.Surface);
        var scope = ScopeOf(session);
        var cells = await reconciler.LoadTouchedCellsAsync(draftSessionId);

        var applied = new List<CellRef>();
        var skipped = new List<CellRef>();
        var unauthorized = new List<CellRef>();
        var issues = new List<DraftValidationIssue>();
        var notifyGroups = new HashSet<string>(StringComparer.Ordinal);

        // Spec F §5: EVERYTHING below is inside the transaction (closes the drift TOCTOU — the live re-read and
        // the reconcile write are not separable by another writer). Per-cell skip replaces all-or-nothing abort.
        await using var tx = await _db.Database.BeginTransactionAsync();

        foreach (var cell in cells)
        {
            if (cell.Staged == cell.Baseline) continue; // untouched-in-effect ⇒ never a conflict, nothing to apply
            var cellRef = Describe(scope.Surface, cell.Cell);

            // (a) Drift check — per-cell SKIP (do NOT abort the commit). This is the locked policy (F §2.1).
            var live = await reconciler.ReadLiveAsync(scope, cell.Cell);
            if (live != cell.Baseline) { skipped.Add(cellRef); continue; }

            // (b) Re-authorize the cell at commit — grants are NOT trusted from stage time (F §5b, §7).
            if (!await reconciler.AuthorizeCellAsync(actingUserId, scope, cell.Cell)) { unauthorized.Add(cellRef); continue; }

            // (c) Reconcile staged→live; collect per-user hard-error skips (the rest of the cell still applies).
            var cellIssues = await reconciler.ReconcileCellAsync(actingUserId, scope, cell);
            if (cellIssues.Count > 0) issues.AddRange(cellIssues);
            applied.Add(cellRef);
            foreach (var g in reconciler.NotifyGroupsFor(scope, cell.Cell)) notifyGroups.Add(g);
        }

        session.Status = DraftSessionStatus.Committed;
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        // Spec F §5.6: after the commit succeeds, fire SignalR once per distinct group (best-effort).
        if (notifyGroups.Count > 0)
        {
            try { await reconciler.BroadcastAsync(notifyGroups); }
            catch { /* real-time notify is best-effort — a failed broadcast must not fail an applied commit */ }
        }

        return new DraftCommitResult(true, applied, skipped, unauthorized, issues, notifyGroups.ToList());
    }

    private static CellRef Describe(DraftSurface surface, CellKey cell) =>
        new(surface, cell.RowId, cell.Date,
            string.Concat(surface.ToString(), ":", cell.RowId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                          "@", cell.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
}
