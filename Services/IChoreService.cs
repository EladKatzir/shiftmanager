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
}
