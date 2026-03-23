namespace ShiftManager.Services;

/// <summary>
/// Severity of a validation issue.
/// Errors are hard blocks; Warnings can be overridden with a signed token.
/// </summary>
public enum ValidationSeverity { Error, Warning }

/// <summary>
/// Category of validation check for UI grouping and override scoping.
/// </summary>
public enum ValidationCategory { JobType, ShiftGrouping, WeeklyHours, RestHours, Trainee, Concurrency, TechShift, Overlap, TimeOff, ChoreConflict, OnDutyConflict, HomeConflict }

/// <summary>
/// A single validation issue found during shift assignment checks.
/// </summary>
public record ValidationIssue(
    string Key,                    // e.g. "JOB_TYPE_MISMATCH"
    string Message,                // localized display message
    ValidationSeverity Severity,
    ValidationCategory Category);

/// <summary>
/// Result of validating a shift assignment.
/// CanAssign is true only if there are no Errors (warnings alone don't block).
/// </summary>
public record ShiftAssignmentValidation(
    bool CanAssign,
    IReadOnlyList<ValidationIssue> Errors,
    IReadOnlyList<ValidationIssue> Warnings)
{
    /// <summary>
    /// True if there are neither errors nor warnings.
    /// </summary>
    public bool IsClean => Errors.Count == 0 && Warnings.Count == 0;
}

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
    /// Returns categorized Errors (hard blocks) and Warnings (overrideable).
    /// </summary>
    Task<ShiftAssignmentValidation> ValidateShiftAssignmentAsync(int userId, int shiftInstanceId);

    /// <summary>
    /// Validates a trainee assignment (self-training, same company, trainee role).
    /// </summary>
    Task<ShiftAssignmentValidation> ValidateTraineeAssignmentAsync(int traineeUserId, int assignmentId);

    /// <summary>
    /// Assigns a user to a shift instance. Validates first unless a valid override token is provided.
    /// </summary>
    Task<ShiftAssignmentResult> AssignShiftAsync(int userId, int shiftInstanceId, int assignedByUserId, string? overrideToken = null, string? notes = null);

    /// <summary>
    /// Unassigns a user from a shift instance.
    /// </summary>
    Task<bool> UnassignShiftAsync(int userId, int shiftInstanceId, int unassignedByUserId, string? reason = null);

    /// <summary>
    /// Generates a time-limited HMAC-SHA256 override token scoped to exact assignment parameters.
    /// Token is valid for 5 minutes and tied to the specific shiftId + userId + warning keys.
    /// </summary>
    string GenerateOverrideToken(int shiftInstanceId, int userId, IReadOnlyList<string> warningKeys);

    /// <summary>
    /// Validates an override token against the given parameters.
    /// </summary>
    bool ValidateOverrideToken(string token, int shiftInstanceId, int userId);

    /// <summary>
    /// Batch validation for FillRange: pre-loads shifts, users, and existing assignments
    /// in 3-4 queries instead of N individual lookups.
    /// </summary>
    Task<Dictionary<(int shiftInstanceId, int userId), ShiftAssignmentValidation>>
        ValidateBatchAsync(IEnumerable<(int shiftInstanceId, int userId)> assignments);
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

public record ShiftAssignmentResult(
    bool Success,
    int? AssignmentId,
    string? ErrorKey,
    string? ErrorMessage,
    ShiftAssignmentValidation? Validation = null
);
