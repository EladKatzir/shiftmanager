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
        return await _db.JobTypes.IgnoreQueryFilters()
            .Include(jt => jt.Area)
            .Include(jt => jt.Molecule)
            .FirstOrDefaultAsync(jt => jt.Id == jobTypeId && jt.IsActive);
    }

    public async Task<List<JobType>> GetJobTypesForMoleculeAsync(int moleculeId)
    {
        var molecule = await _db.Molecules.FirstOrDefaultAsync(m => m.Id == moleculeId);
        if (molecule == null) return new List<JobType>();

        var query = _db.JobTypes.IgnoreQueryFilters()
            .Where(jt => jt.IsActive && jt.AreaId == molecule.AreaId);

        // Tech molecules have their own molecule-scoped job types — show only those.
        // Workforce molecules use area-wide job types (MoleculeId == null).
        if (molecule.Type == Models.Support.MoleculeType.Tech)
            query = query.Where(jt => jt.MoleculeId == moleculeId);
        else
            query = query.Where(jt => jt.MoleculeId == null || jt.MoleculeId == moleculeId);

        return await query
            .OrderBy(jt => jt.SortOrder)
            .ThenBy(jt => jt.Name)
            .ToListAsync();
    }

    public async Task<List<JobType>> GetAllJobTypesAsync()
    {
        return await _db.JobTypes.IgnoreQueryFilters()
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

        // Validate the user can have this job type (same area/molecule)
        if (await ValidateJobTypeForUserAsync(userId, jobTypeId) != JobTypeValidationResult.Valid)
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

        // Validate the user can have this job type (same area/molecule)
        if (await ValidateJobTypeForUserAsync(userId, newJobTypeId) != JobTypeValidationResult.Valid)
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
    public async Task<JobTypeValidationResult> ValidateJobTypeForUserAsync(int userId, int jobTypeId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null)
            return JobTypeValidationResult.UserNotFound;

        var jobType = await GetJobTypeAsync(jobTypeId);
        if (jobType == null)
            return JobTypeValidationResult.JobTypeNotFound;

        // Get user's hierarchy context to determine their area
        var userContext = await _hierarchyService.GetUserHierarchyContextAsync(userId);
        if (userContext == null)
            return JobTypeValidationResult.UserNotFound;

        // Job type must be in the same area as the user
        if (jobType.AreaId != userContext.Path.Area.Id)
            return JobTypeValidationResult.AreaMismatch;

        // If job type is molecule-specific, user must be in that molecule
        if (jobType.MoleculeId.HasValue)
        {
            var company = await _db.Companies.IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == user.CompanyId);
            if (company == null || company.MoleculeId != jobType.MoleculeId)
                return JobTypeValidationResult.MoleculeMismatch;
        }

        return JobTypeValidationResult.Valid;
    }

    // CRUD and admin operations
    public async Task<JobType> CreateJobTypeAsync(string name, int areaId, TimeOnly start, TimeOnly end, bool isActive = true)
    {
        var jobType = new JobType
        {
            Name = name,
            DisplayName = name,
            AreaId = areaId,
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow
        };

        _db.JobTypes.Add(jobType);
        await _db.SaveChangesAsync();
        return jobType;
    }

    public async Task<bool> ToggleActiveAsync(int jobTypeId)
    {
        var jobType = await _db.JobTypes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(jt => jt.Id == jobTypeId);

        if (jobType == null)
            return false;

        jobType.IsActive = !jobType.IsActive;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteJobTypeAsync(int jobTypeId)
    {
        var jobType = await _db.JobTypes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(jt => jt.Id == jobTypeId);

        if (jobType == null)
            return false;

        _db.JobTypes.Remove(jobType);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<int> GetJobTypeCountAsync()
    {
        return await _db.JobTypes.IgnoreQueryFilters()
            .CountAsync();
    }

    public async Task<List<JobType>> GetAllJobTypesWithAreaAsync()
    {
        return await _db.JobTypes.IgnoreQueryFilters()
            .Include(jt => jt.Area)
            .OrderBy(jt => jt.Area.Name)
            .ThenBy(jt => jt.SortOrder)
            .ThenBy(jt => jt.Name)
            .ToListAsync();
    }

    public async Task<List<JobType>> GetAllJobTypesWithHierarchyAsync()
    {
        return await _db.JobTypes.IgnoreQueryFilters()
            .Include(jt => jt.Area)
                .ThenInclude(a => a.Project)
            .Include(jt => jt.Molecule)
            .AsNoTracking()
            .ToListAsync();
    }
}
