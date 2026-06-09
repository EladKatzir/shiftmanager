using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>One staged shift-cell in a draft: the desired assignees for (ShiftTypeId, WorkDate).</summary>
public record DraftOverlayCell(int ShiftTypeId, DateOnly WorkDate, List<int> UserIds);

/// <summary>A touched cell whose live state drifted from the draft baseline (optimistic conflict).</summary>
public record DraftConflict(int ShiftTypeId, DateOnly WorkDate);

/// <summary>A staged assignment a validation pass rejected at commit (e.g. a double-booking).</summary>
public record DraftValidationIssue(int ShiftTypeId, DateOnly WorkDate, int UserId, string Message);

/// <summary>
/// Outcome of committing a draft. When <see cref="Conflicts"/> is non-empty NOTHING is applied
/// (resolve-and-retry). <see cref="ValidationIssues"/> lists staged assignments that were skipped
/// because they failed validation; everything else in those cells still applied.
/// </summary>
public record DraftCommitResult(bool Committed, int AppliedCells, List<DraftConflict> Conflicts, List<DraftValidationIssue> ValidationIssues);

/// <summary>
/// Draft Mode (Epic 4): a private, per-assigner sandbox over one molecule+week of the shift calendar.
/// Staging never touches the live board or fires SignalR; committing reconciles only the touched cells
/// into live with optimistic, per-cell conflict detection plus a final validation pass.
/// </summary>
public interface IDraftModeService
{
    Task<DraftSession?> GetActiveDraftAsync(int ownerUserId, int moleculeId, int? jobTypeId, DateOnly weekStart);
    Task<DraftSession> EnterDraftAsync(int ownerUserId, int moleculeId, int? jobTypeId, DateOnly weekStart, DateOnly weekEnd);

    /// <summary>Stages adding a user to a shift-cell. Captures the live baseline on first touch.</summary>
    Task<bool> StageAssignAsync(int draftSessionId, int shiftTypeId, DateOnly date, int userId);
    /// <summary>Stages removing a user from a shift-cell. Captures the live baseline on first touch.</summary>
    Task<bool> StageClearAsync(int draftSessionId, int shiftTypeId, DateOnly date, int userId);

    /// <summary>The staged assignees for every touched cell — used to overlay the calendar render.</summary>
    Task<List<DraftOverlayCell>> GetOverlayAsync(int draftSessionId);

    Task<DraftCommitResult> CommitAsync(int draftSessionId, int actingUserId);
    Task DiscardAsync(int draftSessionId);
}
