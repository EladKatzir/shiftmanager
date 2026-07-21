namespace ShiftManager.Services;

/// <summary>
/// Service for managing trainee shadowing assignments
/// </summary>
public interface ITraineeService
{
    /// <summary>
    /// Assign a trainee to shadow an employee's shift
    /// </summary>
    Task<bool> AssignTraineeToShiftAsync(int shiftAssignmentId, int traineeUserId, int assignedByUserId);

    /// <summary>
    /// Remove a trainee from a shift
    /// </summary>
    Task<bool> RemoveTraineeFromShiftAsync(int shiftAssignmentId, string reason, int removedByUserId);

    /// <summary>
    /// Validate if a trainee can be assigned to a specific shift
    /// </summary>
    Task<(bool IsValid, string? ErrorMessage)> ValidateTraineeAssignmentAsync(int shiftAssignmentId, int traineeUserId);

    /// <summary>
    /// Get all shifts a trainee is shadowing within a date range
    /// </summary>
    Task<List<Models.ShiftAssignment>> GetTraineeShadowedShiftsAsync(int traineeUserId, DateTime startDate, DateTime endDate);

    /// <summary>
    /// Cancel all shadowing assignments for a user (used when role changes)
    /// </summary>
    Task<int> CancelAllShadowingAssignmentsAsync(int userId, string reason, int changedByUserId);

    /// <summary>
    /// Cancel shadowing assignments that overlap with a time off period
    /// </summary>
    Task<int> CancelShadowingForTimeOffAsync(int traineeUserId, DateTime startDate, DateTime endDate);

    /// <summary>
    /// Get all trainees across a molecule, optionally scoped to a job type (a null JobTypeId trainee is
    /// always included as a universal-relevance safety net). Pass jobTypeId = null for no job-type filter.
    /// </summary>
    Task<List<Models.AppUser>> GetMoleculeTraineesAsync(int moleculeId, int? jobTypeId);
}
