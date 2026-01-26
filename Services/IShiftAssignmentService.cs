namespace ShiftManager.Services;

public interface IShiftAssignmentService
{
    /// <summary>
    /// Gets users eligible for a specific shift instance based on JobType and ShiftGrouping.
    /// </summary>
    Task<List<EligibleUserDto>> GetEligibleUsersForShiftAsync(int shiftInstanceId);

    /// <summary>
    /// Gets users eligible for a shift type (before instance creation).
    /// </summary>
    Task<List<EligibleUserDto>> GetEligibleUsersForShiftTypeAsync(int shiftTypeId, int? jobTypeId = null, int? shiftGroupingId = null);

    /// <summary>
    /// Validates if a user can be assigned to a specific shift.
    /// </summary>
    Task<ShiftAssignmentValidation> ValidateShiftAssignmentAsync(int userId, int shiftInstanceId);

    /// <summary>
    /// Assigns a user to a shift instance.
    /// </summary>
    Task<ShiftAssignmentResult> AssignShiftAsync(int userId, int shiftInstanceId, int assignedByUserId, string? notes = null);

    /// <summary>
    /// Unassigns a user from a shift instance.
    /// </summary>
    Task<bool> UnassignShiftAsync(int userId, int shiftInstanceId, int unassignedByUserId, string? reason = null);
}

public record EligibleUserDto(
    int UserId,
    string DisplayName,
    string? JobTypeName,
    string? CompanyName,
    bool IsInShiftGrouping,
    int AssignedShiftsThisWeek,
    double HoursThisWeek
);

public record ShiftAssignmentValidation(
    bool IsValid,
    string? ErrorKey,
    string? ErrorMessage,
    bool HasJobTypeMismatch,
    bool NotInShiftGrouping,
    bool ExceedsWeeklyCap,
    bool RestHoursViolation
);

public record ShiftAssignmentResult(
    bool Success,
    int? AssignmentId,
    string? ErrorKey,
    string? ErrorMessage
);
