using ShiftManager.Models;

namespace ShiftManager.Services;

public interface IShiftGroupingService
{
    // Query operations
    Task<ShiftGrouping?> GetGroupingAsync(int groupingId);
    Task<List<ShiftGrouping>> GetGroupingsAsync(int moleculeId);
    Task<List<ShiftGrouping>> GetGroupingsForCompanyAsync(int companyId);
    Task<List<ShiftGrouping>> GetGroupingsForJobTypeAsync(int jobTypeId);

    // Create/Update operations
    Task<ShiftGrouping?> CreateGroupingAsync(int moleculeId, string name, string displayName,
        List<int>? companyIds = null, List<int>? jobTypeIds = null);
    Task<bool> UpdateGroupingAsync(int groupingId, string? name = null, string? displayName = null,
        List<int>? companyIds = null, List<int>? jobTypeIds = null);
    Task<bool> DeactivateGroupingAsync(int groupingId);

    // Company membership
    Task<bool> AddCompanyToGroupingAsync(int groupingId, int companyId);
    Task<bool> RemoveCompanyFromGroupingAsync(int groupingId, int companyId);
    Task<List<Company>> GetCompaniesInGroupingAsync(int groupingId);

    // JobType membership
    Task<bool> AddJobTypeToGroupingAsync(int groupingId, int jobTypeId);
    Task<bool> RemoveJobTypeFromGroupingAsync(int groupingId, int jobTypeId);
    Task<List<JobType>> GetJobTypesInGroupingAsync(int groupingId);

    // User queries (users who match the grouping criteria)
    Task<List<AppUser>> GetUsersInGroupingAsync(int groupingId);
    Task<List<AppUser>> GetEligibleUsersForGroupingAsync(int groupingId);
}
