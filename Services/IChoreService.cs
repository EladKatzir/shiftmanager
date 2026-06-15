using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Models.Validation;

namespace ShiftManager.Services;

public interface IChoreService
{
    Task<(bool Success, string Message, Chore? Chore, BusyValidation? Validation, string? OverrideToken)> CreateChoreAsync(int assigneeId, DateOnly date, string title, string? notes = null, bool forceAssign = false, int? moleculeId = null, int? choreTypeId = null, string? overrideToken = null);
    Task<(bool Success, string Message)> CancelChoreAsync(int choreId, string? reason = null);
    Task<(bool Success, string Message)> RestoreChoreAsync(int choreId);
    Task<(bool Success, string Message, Chore? Chore)> ReplaceShiftWithChoreAsync(int shiftAssignmentId, string title, string? notes = null);
    Task<(bool Success, string Message)> ReplaceChoreWithShiftAsync(int choreId, int shiftInstanceId);
    Task<List<Chore>> GetChoresAsync(DateOnly? startDate = null, DateOnly? endDate = null, int? userId = null, bool? includeCancel = false, int? moleculeId = null);
    Task<Chore?> GetChoreByIdAsync(int choreId);
    Task<bool> HasActiveChoreOnDateAsync(int userId, DateOnly date);
    Task<bool> HasShiftOnDateAsync(int userId, DateOnly date);
    Task<ShiftAssignment?> GetShiftOnDateAsync(int userId, DateOnly date);
    Task<bool> HasVacationConflictAsync(int userId, DateOnly date);
    Task<(bool HasConflict, DateOnly? StartDate, DateOnly? EndDate, TimeOffType? Type)> GetVacationConflictDetailsAsync(int userId, DateOnly date);
    Task<bool> CanUserManageChoresAsync(int userId);
    Task<bool> CanUserManageChoreForAssigneeAsync(int managerId, int assigneeId);
    Task<List<AppUser>> GetEligibleAssigneesAsync();

    /// <summary>
    /// Phase 2d: validate a hypothetical chore assignment without writing. Used by the Justice
    /// drawer's eligibility ranking on the Chores calendar. Delegates to <see cref="IBusyService.ValidateAsync"/>
    /// with a <c>BusyTarget.Chore</c>; the existing busy logic enforces molecule boundary,
    /// vacation overlap, cross-resource conflicts, and duplicate-chore detection.
    /// </summary>
    Task<ChoreAssignmentValidation> ValidateChoreAssignmentAsync(
        int userId,
        DateOnly date,
        int moleculeId,
        int? choreTypeId = null,
        string? overrideToken = null,
        CancellationToken ct = default);

    /// <summary>
    /// Phase 2: stamps a <see cref="ChoreTemplate"/> across a date range. Loops the existing manual
    /// <see cref="CreateChoreAsync"/> per (date, assignee); each validates independently. A day that
    /// hard-errors (or otherwise fails) for an assignee is skipped and reported — never throws for a
    /// single bad day. Respects the one-active-chore-per-user-per-day unique index per stamp. Rejects
    /// outright (STAMP_TOO_LARGE) before any work when the date span exceeds 92 days OR the total
    /// prospective chores exceeds 500.
    /// </summary>
    /// <param name="rotate">true → round-robin: one assignee per date, cycling through
    /// <paramref name="assigneeIds"/>. false → fan-out: every assignee on every matching date.</param>
    Task<StampResult> StampTemplateAsync(
        int templateId,
        DateOnly from,
        DateOnly to,
        IReadOnlyList<DayOfWeek> weekdays,
        IReadOnlyList<int> assigneeIds,
        bool rotate);

    /// <summary>
    /// Phase 2: evaluates whether a candidate is eligible for a chore type (gender/officer/exempt),
    /// for the assignment picker's "greyed + reason" affordance. Pure eligibility only — does NOT
    /// check date conflicts (the picker layers BusyService on top). Free-text (choreTypeId resolving
    /// to no rules) returns eligible.
    /// </summary>
    Task<EligibilityResult> GetEligibilityForCandidateAsync(int userId, int choreTypeId);
}

/// <summary>One chore that <see cref="IChoreService.StampTemplateAsync"/> created.</summary>
public sealed record StampCreated(DateOnly Date, int UserId, int ChoreId);

/// <summary>One (date, user) the stamp could NOT create, with a stable reason key for localization.</summary>
public sealed record StampSkipped(DateOnly Date, int UserId, string ReasonKey);

/// <summary>Summary of a template stamp: what was created and what was skipped (and why).</summary>
public sealed record StampResult(
    IReadOnlyList<StampCreated> Created,
    IReadOnlyList<StampSkipped> Skipped)
{
    public int CreatedCount => Created.Count;
    public int SkippedCount => Skipped.Count;
}
