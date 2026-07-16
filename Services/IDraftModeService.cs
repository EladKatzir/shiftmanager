using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// One staged shift-cell in a draft: the desired assignees for (ShiftTypeId, WorkDate) plus the desired
/// primary→trainee shadow pairings (sub-project A). <see cref="Trainees"/> maps a staged primaryUserId to
/// the traineeUserId shadowing it; a primary absent from the map has no staged trainee.
/// </summary>
public record DraftOverlayCell(int ShiftTypeId, DateOnly WorkDate, List<int> UserIds, Dictionary<int, int> Trainees);

/// <summary>
/// Draft Mode (Epic 4) — the SHIFTS staging + overlay surface. Staging never touches the live board or fires
/// SignalR. Entering/discarding/committing a draft is handled by the shared <see cref="IDraftLifecycle"/>
/// (the shift commit reconciler is <see cref="IDraftReconciler"/> implemented by <c>DraftModeService</c>);
/// this interface is just the shifts-board write path (stage assign/clear) + the render overlay read path.
/// </summary>
public interface IDraftModeService
{
    /// <summary>The caller's Active shift draft for (molecule, jobType, week), or null.</summary>
    Task<DraftSession?> GetActiveDraftAsync(int ownerUserId, int moleculeId, int? jobTypeId, DateOnly weekStart);

    /// <summary>Stages adding a user to a shift-cell. Captures the live baseline on first touch.</summary>
    Task<bool> StageAssignAsync(int draftSessionId, int shiftTypeId, DateOnly date, int userId);
    /// <summary>
    /// Stages removing a user from a shift-cell. Captures the live baseline on first touch. Also drops any
    /// staged trainee shadowing that primary — a trainee cannot outlive its primary (edge-case ruling 1).
    /// </summary>
    Task<bool> StageClearAsync(int draftSessionId, int shiftTypeId, DateOnly date, int userId);

    /// <summary>
    /// Stages a trainee shadowing a (staged or live) primary on a shift-cell (sub-project A). Coordinate-keyed
    /// — never an assignmentId — because a staged primary has no persisted row. Captures the live baseline
    /// (primaries + trainees) on first touch. A primary shadows at most one trainee (last write wins).
    /// </summary>
    Task<bool> StageTraineeAsync(int draftSessionId, int shiftTypeId, DateOnly date, int primaryUserId, int traineeUserId);
    /// <summary>Stages clearing the trainee shadowing <paramref name="primaryUserId"/> on a shift-cell.</summary>
    Task<bool> StageTraineeClearAsync(int draftSessionId, int shiftTypeId, DateOnly date, int primaryUserId);

    /// <summary>The staged assignees for every touched cell — used to overlay the calendar render.</summary>
    Task<List<DraftOverlayCell>> GetOverlayAsync(int draftSessionId);
}
