namespace ShiftManager.Services;

// ValidationIssue, ValidationSeverity, and ValidationCategory previously lived here.
// They were promoted to ShiftManager.Models.Validation as part of the project-wide
// error-handling overhaul so non-shift services can use them. The types are still
// usable here unqualified thanks to the global using in GlobalUsings.cs.

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
    /// When <paramref name="categoryFilter"/> is true (3b), eligibility is DoesShifts (per-company
    /// membership, mirror fallback) + the shift's ShiftCategory membership instead of the legacy
    /// jobType filter; a null CategoryId returns all participants (the shared-shift fallback).
    /// </summary>
    Task<List<EligibleUserDto>> GetEligibleUsersForShiftTypeAsync(int shiftTypeId, int? jobTypeId = null, int? shiftGroupingId = null, bool categoryFilter = false);

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
    /// Draft Mode overload (sub-project A): validates a trainee against a primary + shift instance directly,
    /// without needing a persisted assignment row — a freshly reconciled slot may still be unsaved at commit.
    /// </summary>
    Task<ShiftAssignmentValidation> ValidateTraineeAssignmentAsync(int traineeUserId, int primaryUserId, int shiftInstanceId);

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
