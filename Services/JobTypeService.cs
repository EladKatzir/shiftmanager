using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Results;
using ShiftManager.Resources;

namespace ShiftManager.Services;

/// <summary>
/// Manages JobType definitions and user-to-job-type assignments.
///
/// Migrated to OperationResult as part of the project-wide error-handling overhaul.
/// Previously this service had silent <c>return false</c> failures with no logging — those
/// are now structured failures with localized messages keyed under <c>Error_JobTypeService_*</c>.
/// </summary>
public class JobTypeService : IJobTypeService
{
    private readonly AppDbContext _db;
    private readonly IHierarchyService _hierarchyService;
    private readonly ILogger<JobTypeService> _logger;
    private readonly IStringLocalizer<SharedResources> _localizer;

    public JobTypeService(
        AppDbContext db,
        IHierarchyService hierarchyService,
        ILogger<JobTypeService> logger,
        IStringLocalizer<SharedResources> localizer)
    {
        _db = db;
        _hierarchyService = hierarchyService;
        _logger = logger;
        _localizer = localizer;
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

        var isWorkforce = molecule.Type == Models.Support.MoleculeType.Workforce
                        || molecule.Type == Models.Support.MoleculeType.Helper;

        return await _db.JobTypes.IgnoreQueryFilters()
            .Where(jt => jt.IsActive
                && jt.AreaId == molecule.AreaId
                && (jt.MoleculeId == null || jt.MoleculeId == moleculeId)
                && (isWorkforce || !jt.IsWorkforceOnly))
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
        // SECURITY-AUDITED: SAFE — read-only display lookup; cross-tenant resolution is needed
        // so area-scope managers see correct results for users in other companies.
        var user = await _db.Users.IgnoreQueryFilters()
            .Include(u => u.JobType)
            .FirstOrDefaultAsync(u => u.Id == userId);

        return user?.JobType;
    }

    public async Task<List<AppUser>> GetUsersWithJobTypeAsync(int jobTypeId)
    {
        // SECURITY-AUDITED: SAFE — read-only enumeration. JobTypes are area-scoped so callers
        // (area-Director, etc.) legitimately need cross-company results matching the JobType.
        return await _db.Users.IgnoreQueryFilters()
            .Where(u => u.JobTypeId == jobTypeId && u.IsActive)
            .OrderBy(u => u.DisplayName)
            .ToListAsync();
    }

    // Assignment operations
    public async Task<OperationResult> AssignJobTypeAsync(int userId, int jobTypeId, int? assignedByUserId = null)
    {
        // SECURITY-AUDITED: SAFE — area-Director job-type management legitimately targets users
        // across companies in the area; tenant filter would silently lock out cross-tenant edits.
        var user = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            _logger.LogWarning("AssignJobTypeAsync: user {UserId} not found", userId);
            return OperationResult.Fail(
                "Error_JobTypeService_UserNotFound",
                _localizer["Error_JobTypeService_UserNotFound"].Value);
        }

        var jobType = await GetJobTypeAsync(jobTypeId);
        if (jobType == null)
        {
            _logger.LogWarning("AssignJobTypeAsync: jobType {JobTypeId} not found", jobTypeId);
            return OperationResult.Fail(
                "Error_JobTypeService_NotFound",
                _localizer["Error_JobTypeService_NotFound"].Value);
        }

        var validation = await ValidateJobTypeForUserAsync(userId, jobTypeId);
        if (validation != JobTypeValidationResult.Valid)
        {
            return ValidationResultToOperationResult(validation);
        }

        user.JobTypeId = jobTypeId;
        await _db.SaveChangesAsync();

        _logger.LogInformation("User {UserId} assigned JobType {JobTypeId} by {AssignedBy}",
            userId, jobTypeId, assignedByUserId);
        return OperationResult.Ok();
    }

    public async Task<OperationResult> ChangeJobTypeAsync(int userId, int newJobTypeId, int? changedByUserId = null)
    {
        // SECURITY-AUDITED: SAFE — see AssignJobTypeAsync. Authorization for cross-company
        // job-type changes is enforced upstream by the caller's grant scope.
        var user = await _db.Users.IgnoreQueryFilters()
            .Include(u => u.JobType)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            _logger.LogWarning("ChangeJobTypeAsync: user {UserId} not found", userId);
            return OperationResult.Fail(
                "Error_JobTypeService_UserNotFound",
                _localizer["Error_JobTypeService_UserNotFound"].Value);
        }

        var newJobType = await GetJobTypeAsync(newJobTypeId);
        if (newJobType == null)
        {
            _logger.LogWarning("ChangeJobTypeAsync: jobType {JobTypeId} not found", newJobTypeId);
            return OperationResult.Fail(
                "Error_JobTypeService_NotFound",
                _localizer["Error_JobTypeService_NotFound"].Value);
        }

        var validation = await ValidateJobTypeForUserAsync(userId, newJobTypeId);
        if (validation != JobTypeValidationResult.Valid)
        {
            return ValidationResultToOperationResult(validation);
        }

        user.JobTypeId = newJobTypeId;
        await _db.SaveChangesAsync();

        _logger.LogInformation("User {UserId} JobType changed to {NewJobTypeId} by {ChangedBy}",
            userId, newJobTypeId, changedByUserId);
        return OperationResult.Ok();
    }

    public async Task<OperationResult> RemoveJobTypeAsync(int userId, int? removedByUserId = null)
    {
        // SECURITY-AUDITED: SAFE — see AssignJobTypeAsync.
        var user = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            _logger.LogWarning("RemoveJobTypeAsync: user {UserId} not found", userId);
            return OperationResult.Fail(
                "Error_JobTypeService_UserNotFound",
                _localizer["Error_JobTypeService_UserNotFound"].Value);
        }

        user.JobTypeId = null;
        await _db.SaveChangesAsync();

        _logger.LogInformation("User {UserId} JobType removed by {RemovedBy}", userId, removedByUserId);
        return OperationResult.Ok();
    }

    private OperationResult ValidationResultToOperationResult(JobTypeValidationResult validation)
    {
        return validation switch
        {
            JobTypeValidationResult.UserNotFound => OperationResult.Fail(
                "Error_JobTypeService_UserNotFound",
                _localizer["Error_JobTypeService_UserNotFound"].Value),
            JobTypeValidationResult.JobTypeNotFound => OperationResult.Fail(
                "Error_JobTypeService_NotFound",
                _localizer["Error_JobTypeService_NotFound"].Value),
            JobTypeValidationResult.AreaMismatch => OperationResult.Fail(
                "Error_JobTypeService_AreaMismatch",
                _localizer["Error_JobTypeService_AreaMismatch"].Value),
            JobTypeValidationResult.MoleculeMismatch => OperationResult.Fail(
                "Error_JobTypeService_MoleculeMismatch",
                _localizer["Error_JobTypeService_MoleculeMismatch"].Value),
            _ => OperationResult.Fail(
                "Error_JobTypeService_ValidationFailed",
                _localizer["Error_JobTypeService_ValidationFailed"].Value)
        };
    }

    // Validation
    public async Task<JobTypeValidationResult> ValidateJobTypeForUserAsync(int userId, int jobTypeId)
    {
        // SECURITY-AUDITED: SAFE — read-only validation; cross-tenant lookup needed so callers
        // with area/project scope get accurate validation rather than a misleading "user not found".
        var user = await _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
            return JobTypeValidationResult.UserNotFound;

        var jobType = await GetJobTypeAsync(jobTypeId);
        if (jobType == null)
            return JobTypeValidationResult.JobTypeNotFound;

        var userContext = await _hierarchyService.GetUserHierarchyContextAsync(userId);
        if (userContext == null)
            return JobTypeValidationResult.UserNotFound;

        if (jobType.AreaId != userContext.Path.Area.Id)
            return JobTypeValidationResult.AreaMismatch;

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
    public async Task<OperationResult<JobType>> CreateJobTypeAsync(string name, int areaId, TimeOnly start, TimeOnly end, bool isActive = true)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationResult<JobType>.Fail(
                "Error_JobTypeService_NameRequired",
                _localizer["Error_JobTypeService_NameRequired"].Value);
        }

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
        _logger.LogInformation("Created JobType {JobTypeId} '{Name}' in Area {AreaId}", jobType.Id, name, areaId);
        return OperationResult<JobType>.Ok(jobType);
    }

    public async Task<OperationResult> ToggleActiveAsync(int jobTypeId)
    {
        var jobType = await _db.JobTypes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(jt => jt.Id == jobTypeId);

        if (jobType == null)
        {
            _logger.LogWarning("ToggleActiveAsync: jobType {JobTypeId} not found", jobTypeId);
            return OperationResult.Fail(
                "Error_JobTypeService_NotFound",
                _localizer["Error_JobTypeService_NotFound"].Value);
        }

        jobType.IsActive = !jobType.IsActive;
        await _db.SaveChangesAsync();
        _logger.LogInformation("JobType {JobTypeId} active toggled to {IsActive}", jobTypeId, jobType.IsActive);
        return OperationResult.Ok();
    }

    public async Task<OperationResult> DeleteJobTypeAsync(int jobTypeId)
    {
        var jobType = await _db.JobTypes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(jt => jt.Id == jobTypeId);

        if (jobType == null)
        {
            _logger.LogWarning("DeleteJobTypeAsync: jobType {JobTypeId} not found", jobTypeId);
            return OperationResult.Fail(
                "Error_JobTypeService_NotFound",
                _localizer["Error_JobTypeService_NotFound"].Value);
        }

        _db.JobTypes.Remove(jobType);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Deleted JobType {JobTypeId} '{Name}'", jobTypeId, jobType.Name);
        return OperationResult.Ok();
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
