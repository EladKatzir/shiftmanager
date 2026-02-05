using ShiftManager.Models;

namespace ShiftManager.Services;

public record FyiOverlayData(
    bool HasVacation,
    bool HasChore,
    bool HasOnDuty,
    List<string> OtherShifts
);

public record RestViolationWarning(
    string ShiftName,
    DateOnly Date,
    TimeSpan RestDuration
);

public record AssignmentResult(
    bool Success,
    string? ErrorMessage,
    List<RestViolationWarning> Warnings
);

public interface IShiftCalendarService
{
    // Data loading
    Task<List<AppUser>> GetUsersForCalendarAsync(int moleculeId, int jobTypeId);
    Task<List<ShiftInstance>> GetShiftInstancesAsync(int moleculeId, int jobTypeId, DateOnly start, DateOnly end);
    Task<List<ShiftAssignment>> GetAssignmentsAsync(int moleculeId, int jobTypeId, DateOnly start, DateOnly end);

    // Capacity management
    Task<int> GetCapacityAsync(int shiftTypeId, int moleculeId, int jobTypeId, DateOnly date);
    Task<int> GetDefaultCapacityAsync(int shiftTypeId);
    Task SetCapacityOverrideAsync(int shiftTypeId, int moleculeId, int jobTypeId, DateOnly date, int capacity, int userId);
    Task RemoveCapacityOverrideAsync(int shiftTypeId, int moleculeId, int jobTypeId, DateOnly date);

    // Assignment
    Task<AssignmentResult> AssignUserAsync(int shiftInstanceId, int userId, int assignedByUserId);
    Task<bool> UnassignUserAsync(int shiftAssignmentId, int unassignedByUserId);

    // Validation
    Task<List<RestViolationWarning>> CheckRestViolationsAsync(int userId, DateOnly date, int shiftTypeId);

    // FYI overlays
    Task<Dictionary<(int UserId, DateOnly Date), FyiOverlayData>> GetOverlaysAsync(int moleculeId, DateOnly start, DateOnly end);
}
