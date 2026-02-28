using ShiftManager.Models;

namespace ShiftManager.Services;

public interface IJobTypeService
{
    // Query operations
    Task<JobType?> GetJobTypeAsync(int jobTypeId);
    Task<List<JobType>> GetJobTypesAsync(int areaId);
    /// <summary>
    /// Returns job types applicable to a specific molecule: molecule-specific ones plus
    /// area-wide ones (where MoleculeId is null) from the molecule's area.
    /// </summary>
    Task<List<JobType>> GetJobTypesForMoleculeAsync(int moleculeId);
    Task<List<JobType>> GetAllJobTypesAsync();
    Task<JobType?> GetUserJobTypeAsync(int userId);
    Task<List<AppUser>> GetUsersWithJobTypeAsync(int jobTypeId);

    // Assignment operations
    Task<bool> AssignJobTypeAsync(int userId, int jobTypeId, int? assignedByUserId = null);
    Task<bool> ChangeJobTypeAsync(int userId, int newJobTypeId, int? changedByUserId = null);
    Task<bool> RemoveJobTypeAsync(int userId, int? removedByUserId = null);

    // Validation
    Task<bool> CanUserHaveJobTypeAsync(int userId, int jobTypeId);

    // CRUD and admin operations
    Task<JobType> CreateJobTypeAsync(string name, int areaId, TimeOnly start, TimeOnly end, bool isActive = true);
    Task<bool> ToggleActiveAsync(int jobTypeId);
    Task<bool> DeleteJobTypeAsync(int jobTypeId);
    Task<int> GetJobTypeCountAsync();
    Task<List<JobType>> GetAllJobTypesWithAreaAsync();
    Task<List<JobType>> GetAllJobTypesWithHierarchyAsync();
}
