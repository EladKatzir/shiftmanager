using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>One staged shift-cell in a draft: the desired assignees for (ShiftTypeId, WorkDate).</summary>
public record DraftOverlayCell(int ShiftTypeId, DateOnly WorkDate, List<int> UserIds);

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
    /// <summary>Stages removing a user from a shift-cell. Captures the live baseline on first touch.</summary>
    Task<bool> StageClearAsync(int draftSessionId, int shiftTypeId, DateOnly date, int userId);

    /// <summary>The staged assignees for every touched cell — used to overlay the calendar render.</summary>
    Task<List<DraftOverlayCell>> GetOverlayAsync(int draftSessionId);
}
