using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// The scope a draft session covers. Carries every scope axis across all three surfaces; the axes not used
/// by a given surface are null (shifts: MoleculeId+JobTypeId; chores: MoleculeId; on-call: AreaId as a UI
/// hint only — on-call data is global). See <see cref="DraftSession"/> for the per-surface scope tuple.
/// </summary>
public record DraftScope(
    DraftSurface Surface,
    int? MoleculeId,
    int? JobTypeId,
    int? AreaId,
    DateOnly WeekStart,
    DateOnly WeekEnd);

/// <summary>
/// A surface's cell coordinate. All three surfaces key on (int row, date): shifts = (shiftTypeId, date);
/// chores = (userId, date); on-call = (dutyTypeValue, date).
/// </summary>
public record CellKey(int RowId, DateOnly Date);

/// <summary>
/// One touched cell's canonical state. <see cref="Baseline"/> is the surface's canonical serialization of
/// the live state captured at first touch; <see cref="Staged"/> is the desired state. The lifecycle does
/// plain string equality: <c>Staged == Baseline</c> ⇒ no-op (skip); a live re-read <c>!= Baseline</c> ⇒
/// drift (skip + report). The reconciler owns the serialization format for its surface.
/// </summary>
public record DraftCellState(CellKey Cell, string Baseline, string Staged);

/// <summary>A cell reference for the commit report (Applied / Skipped / Unauthorized), with a display label.</summary>
public record CellRef(DraftSurface Surface, int RowId, DateOnly WorkDate, string Label);

/// <summary>
/// A staged change a validation pass rejected at commit (e.g. a double-booking / rank-ineligible). Its cell
/// still applied the rest of its changes. <see cref="RowId"/> is the surface's row coordinate (shiftTypeId
/// for shifts, userId for chores, dutyTypeValue for on-call).
/// </summary>
public record DraftValidationIssue(int RowId, DateOnly WorkDate, int UserId, string Message);

/// <summary>
/// Outcome of committing a draft (Spec F §5). Under the locked per-cell policy, the session moves to
/// Committed and every conflict-free, authorized cell is applied; drifted cells go to <see cref="Skipped"/>
/// (re-stage them) and revoked-grant cells to <see cref="Unauthorized"/>. <see cref="ValidationIssues"/>
/// lists per-user staged changes skipped inside an otherwise-applied cell. <see cref="NotifiedGroups"/> is
/// the distinct union of applied cells' SignalR groups (also surfaced for tests/diagnostics).
/// </summary>
public record DraftCommitResult(
    bool Committed,
    IReadOnlyList<CellRef> Applied,
    IReadOnlyList<CellRef> Skipped,
    IReadOnlyList<CellRef> Unauthorized,
    IReadOnlyList<DraftValidationIssue> ValidationIssues,
    IReadOnlyList<string> NotifiedGroups);

/// <summary>
/// A per-surface plug-in for the shared <see cref="IDraftLifecycle"/> commit engine (Spec F §4). The
/// lifecycle owns the enter/discard/commit orchestration and transaction; the reconciler owns everything
/// surface-specific: how a cell's live state is serialized, how drift is read, how a cell is authorized at
/// commit, how staged→live is applied, and which SignalR groups a commit notifies.
/// </summary>
public interface IDraftReconciler
{
    /// <summary>Which surface this reconciler serves. The lifecycle selects a reconciler by session surface.</summary>
    DraftSurface Surface { get; }

    /// <summary>Capture the live baseline for a cell (surface-specific canonical string) — used at staging first-touch.</summary>
    Task<string> CaptureBaselineAsync(DraftScope scope, CellKey cell);

    /// <summary>Load every touched cell for the session from this surface's cell table, as canonical states.</summary>
    Task<IReadOnlyList<DraftCellState>> LoadTouchedCellsAsync(int draftSessionId);

    /// <summary>Read the CURRENT live canonical string for drift detection (called again inside the commit tx).</summary>
    Task<string> ReadLiveAsync(DraftScope scope, CellKey cell);

    /// <summary>Re-authorize this specific cell for the acting user AT COMMIT (a grant may have been revoked).</summary>
    Task<bool> AuthorizeCellAsync(int actingUserId, DraftScope scope, CellKey cell);

    /// <summary>Apply staged→live for one cell inside the shared transaction; return per-user hard-error issues.</summary>
    Task<IReadOnlyList<DraftValidationIssue>> ReconcileCellAsync(int actingUserId, DraftScope scope, DraftCellState staged);

    /// <summary>The SignalR group(s) this cell's commit must notify.</summary>
    IEnumerable<string> NotifyGroupsFor(DraftScope scope, CellKey cell);

    /// <summary>Broadcast this surface's real-time "reload" event to each group once (after commit succeeds).</summary>
    Task BroadcastAsync(IEnumerable<string> groups);
}
