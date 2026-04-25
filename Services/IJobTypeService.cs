using ShiftManager.Models;
using ShiftManager.Models.Results;

namespace ShiftManager.Services;

/// <summary>
/// Result of a JobType validation check. Kept as a structured enum (rather than
/// migrated to OperationResult) because callers branch on specific outcomes for UX.
/// </summary>
public enum JobTypeValidationResult
{
    Valid,
    UserNotFound,
    JobTypeNotFound,
    AreaMismatch,
    MoleculeMismatch
}

public interface IJobTypeService
{
    // Query operations (unchanged — null/empty list is the correct "not found" signal)
    Task<JobType?> GetJobTypeAsync(int jobTypeId);

    /// <summary>
    /// Returns job types applicable to a specific molecule: molecule-specific ones plus
    /// area-wide ones (where MoleculeId is null) from the molecule's area.
    /// </summary>
    Task<List<JobType>> GetJobTypesForMoleculeAsync(int moleculeId);

    Task<List<JobType>> GetAllJobTypesAsync();
    Task<JobType?> GetUserJobTypeAsync(int userId);
    Task<List<AppUser>> GetUsersWithJobTypeAsync(int jobTypeId);

    // Assignment operations — migrated to OperationResult so silent failures are eliminated.
    Task<OperationResult> AssignJobTypeAsync(int userId, int jobTypeId, int? assignedByUserId = null);
    Task<OperationResult> ChangeJobTypeAsync(int userId, int newJobTypeId, int? changedByUserId = null);
    Task<OperationResult> RemoveJobTypeAsync(int userId, int? removedByUserId = null);

    // Validation — kept as enum for UX-driven branching at call sites.
    Task<JobTypeValidationResult> ValidateJobTypeForUserAsync(int userId, int jobTypeId);

    // CRUD and admin operations — write methods migrated to OperationResult.
    Task<OperationResult<JobType>> CreateJobTypeAsync(string name, int areaId, TimeOnly start, TimeOnly end, bool isActive = true);
    Task<OperationResult> ToggleActiveAsync(int jobTypeId);
    Task<OperationResult> DeleteJobTypeAsync(int jobTypeId);
    Task<int> GetJobTypeCountAsync();
    Task<List<JobType>> GetAllJobTypesWithAreaAsync();
    Task<List<JobType>> GetAllJobTypesWithHierarchyAsync();
}
