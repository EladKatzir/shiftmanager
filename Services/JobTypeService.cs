using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

public class JobTypeService : IJobTypeService
{
    private readonly AppDbContext _db;
    private readonly IHierarchyService _hierarchyService;

    public JobTypeService(AppDbContext db, IHierarchyService hierarchyService)
    {
        _db = db;
        _hierarchyService = hierarchyService;
    }

    // Query operations
    public async Task<JobType?> GetJobTypeAsync(int jobTypeId)
    {
        return await _db.JobTypes
            .Include(jt => jt.Area)
            .FirstOrDefaultAsync(jt => jt.Id == jobTypeId && jt.IsActive);
    }

    public async Task<List<JobType>> GetJobTypesAsync(int areaId)
    {
        return await _db.JobTypes
            .Where(jt => jt.AreaId == areaId && jt.IsActive)
            .OrderBy(jt => jt.SortOrder)
            .ThenBy(jt => jt.Name)
            .ToListAsync();
    }

    public async Task<List<JobType>> GetAllJobTypesAsync()
    {
        return await _db.JobTypes
            .Include(jt => jt.Area)
            .Where(jt => jt.IsActive)
            .OrderBy(jt => jt.Area.Name)
            .ThenBy(jt => jt.SortOrder)
            .ThenBy(jt => jt.Name)
            .ToListAsync();
    }

    public async Task<JobType?> GetUserJobTypeAsync(int userId)
    {
        var user = await _db.Users
            .Include(u => u.JobType)
            .FirstOrDefaultAsync(u => u.Id == userId);

        return user?.JobType;
    }

    public async Task<List<AppUser>> GetUsersWithJobTypeAsync(int jobTypeId)
    {
        return await _db.Users
            .Where(u => u.JobTypeId == jobTypeId && u.IsActive)
            .OrderBy(u => u.DisplayName)
            .ToListAsync();
    }

    // Assignment operations
    public async Task<bool> AssignJobTypeAsync(int userId, int jobTypeId, int? assignedByUserId = null)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null)
            return false;

        var jobType = await GetJobTypeAsync(jobTypeId);
        if (jobType == null)
            return false;

        // Validate the user can have this job type (same area)
        if (!await CanUserHaveJobTypeAsync(userId, jobTypeId))
            return false;

        user.JobTypeId = jobTypeId;
        await _db.SaveChangesAsync();

        return true;
    }

    public async Task<bool> ChangeJobTypeAsync(int userId, int newJobTypeId, int? changedByUserId = null)
    {
        var user = await _db.Users
            .Include(u => u.JobType)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
            return false;

        var newJobType = await GetJobTypeAsync(newJobTypeId);
        if (newJobType == null)
            return false;

        // Validate the user can have this job type (same area)
        if (!await CanUserHaveJobTypeAsync(userId, newJobTypeId))
            return false;

        var oldJobTypeId = user.JobTypeId;

        // Update job type
        user.JobTypeId = newJobTypeId;
        await _db.SaveChangesAsync();

        // Note: Grant removal on job type change should be handled by GrantService
        // when it's implemented. For now, we just update the job type.

        return true;
    }

    public async Task<bool> RemoveJobTypeAsync(int userId, int? removedByUserId = null)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null)
            return false;

        user.JobTypeId = null;
        await _db.SaveChangesAsync();

        return true;
    }

    // Validation
    public async Task<bool> CanUserHaveJobTypeAsync(int userId, int jobTypeId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null)
            return false;

        var jobType = await GetJobTypeAsync(jobTypeId);
        if (jobType == null)
            return false;

        // Get user's hierarchy context to determine their area
        var userContext = await _hierarchyService.GetUserHierarchyContextAsync(userId);
        if (userContext == null)
            return false;

        // Job type must be in the same area as the user
        return jobType.AreaId == userContext.Path.Area.Id;
    }
}
