using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// A named+colored overlay item (chore or on-duty) for rich calendar display.
/// </summary>
public record FyiOverlayItem(string Name, string? Color);

public record FyiOverlayData(
    bool HasVacation,
    List<FyiOverlayItem> ChoreItems,
    List<FyiOverlayItem> OnDutyItems,
    List<string> OtherShifts,
    // "Day at [X]" time-off (TimeOffType.DayAt): the free-text label the user entered, rendered on the
    // calendar as "יום {Label}" / "Day {Label}" instead of the vacation palm-tree. Null when none. (Issue: day-X)
    string? DayAtLabel = null
)
{
    /// <summary>Backward-compatible: true if any chore items exist.</summary>
    public bool HasChore => ChoreItems.Count > 0;
    /// <summary>Backward-compatible: true if any on-duty items exist.</summary>
    public bool HasOnDuty => OnDutyItems.Count > 0;
};

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
    Task<List<AppUser>> GetUsersForCalendarAsync(int moleculeId, int? jobTypeId);
    Task<List<ShiftInstance>> GetShiftInstancesAsync(int moleculeId, int? jobTypeId, DateOnly start, DateOnly end);
    Task<List<ShiftAssignment>> GetAssignmentsAsync(int moleculeId, int? jobTypeId, DateOnly start, DateOnly end);

    // Capacity management
    Task<int> GetCapacityAsync(int shiftTypeId, int moleculeId, int? jobTypeId, DateOnly date);
    Task<int> GetDefaultCapacityAsync(int shiftTypeId);
    Task SetCapacityOverrideAsync(int shiftTypeId, int moleculeId, int? jobTypeId, DateOnly date, int capacity, int userId);
    Task RemoveCapacityOverrideAsync(int shiftTypeId, int moleculeId, int? jobTypeId, DateOnly date);

    /// <summary>
    /// Batch load capacity overrides for all shift types within a molecule/jobType/date range.
    /// Eliminates N+1 queries when loading capacity for many shift instances.
    /// Returns a dictionary keyed by (ShiftTypeId, Date) with the effective capacity.
    /// </summary>
    Task<Dictionary<(int ShiftTypeId, DateOnly Date), int>> GetCapacitiesBatchAsync(
        int moleculeId, int? jobTypeId, DateOnly start, DateOnly end);

    /// <summary>
    /// Gets users eligible for a specific shift type based on EligibleCompanyIds and RequiresOfficerRank.
    /// When <paramref name="categoryFilter"/> is true (3b), also gates by DoesShifts (per-company
    /// membership, mirror fallback) + the shift's ShiftCategory membership and excludes GroupUser;
    /// officer-rank is still enforced. A null CategoryId returns all participants (shared-shift fallback).
    /// </summary>
    Task<List<AppUser>> GetEligibleUsersForShiftTypeAsync(int moleculeId, int shiftTypeId, bool categoryFilter = false);

    // Assignment
    Task<AssignmentResult> AssignUserAsync(int shiftInstanceId, int userId, int assignedByUserId);
    Task<bool> UnassignUserAsync(int shiftAssignmentId, int unassignedByUserId);

    // Validation
    Task<List<RestViolationWarning>> CheckRestViolationsAsync(int userId, DateOnly date, int shiftTypeId);

    // FYI overlays
    Task<Dictionary<(int UserId, DateOnly Date), FyiOverlayData>> GetOverlaysAsync(int moleculeId, DateOnly start, DateOnly end);
}
