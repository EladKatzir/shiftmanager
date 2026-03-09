using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;

namespace ShiftManager.Services;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — setup tasks are cross-company configuration;
// queries scoped by explicit moleculeId/companyId parameters; called only from Grant:SystemConfiguration-protected pages
public class SetupTaskService : ISetupTaskService
{
    private readonly AppDbContext _db;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly ILogger<SetupTaskService> _logger;
    private readonly IJobTypeService _jobTypeService;

    public SetupTaskService(
        AppDbContext db,
        IStringLocalizer<SharedResources> localizer,
        ILogger<SetupTaskService> logger,
        IJobTypeService jobTypeService)
    {
        _db = db;
        _localizer = localizer;
        _logger = logger;
        _jobTypeService = jobTypeService;
    }

    public async Task<List<SetupTaskDto>> GenerateTasksForMoleculeAsync(int moleculeId, int assignToUserId)
    {
        // SECURITY-AUDITED: SAFE — scoped by specific moleculeId parameter; admin setup wizard
        var molecule = await _db.Molecules.IgnoreQueryFilters()
            .Include(m => m.Area)
            .FirstOrDefaultAsync(m => m.Id == moleculeId);

        if (molecule == null)
            return new List<SetupTaskDto>();

        var tasks = new List<SetupTask>();

        // Task 1: Assign Molecule Admin
        tasks.Add(new SetupTask
        {
            Type = SetupTaskType.AssignMoleculeAdmin,
            Title = _localizer["SetupTask_AssignMoleculeAdmin_Title"],
            Description = string.Format(_localizer["SetupTask_AssignMoleculeAdmin_Desc"], molecule.DisplayName),
            MoleculeId = moleculeId,
            AssignedToUserId = assignToUserId,
            Status = SetupTaskStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });

        // Task 2: Setup Shift Groupings (for Workforce molecules)
        if (molecule.Type == MoleculeType.Workforce)
        {
            tasks.Add(new SetupTask
            {
                Type = SetupTaskType.SetupShiftGroupings,
                Title = _localizer["SetupTask_SetupShiftGroupings_Title"],
                Description = string.Format(_localizer["SetupTask_SetupShiftGroupings_Desc"], molecule.DisplayName),
                MoleculeId = moleculeId,
                AssignedToUserId = assignToUserId,
                Status = SetupTaskStatus.Pending,
                CreatedAt = DateTime.UtcNow
            });

            // Task 3: Assign directors for each job type
            // SECURITY-AUDITED: SAFE — scoped by moleculeId; admin setup wizard
            var jobTypes = await _jobTypeService.GetJobTypesForMoleculeAsync(moleculeId);

            foreach (var jobType in jobTypes)
            {
                var taskType = GetDirectorTaskType(jobType.Name);
                if (taskType.HasValue)
                {
                    tasks.Add(new SetupTask
                    {
                        Type = taskType.Value,
                        Title = string.Format(_localizer[$"SetupTask_{taskType.Value}_Title"], jobType.DisplayName),
                        Description = string.Format(_localizer[$"SetupTask_{taskType.Value}_Desc"], jobType.DisplayName, molecule.DisplayName),
                        MoleculeId = moleculeId,
                        JobTypeId = jobType.Id,
                        AssignedToUserId = assignToUserId,
                        Status = SetupTaskStatus.Pending,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }
        }

        // Task 4: Setup shift blueprints
        tasks.Add(new SetupTask
        {
            Type = SetupTaskType.SetupShiftBlueprints,
            Title = _localizer["SetupTask_SetupShiftBlueprints_Title"],
            Description = string.Format(_localizer["SetupTask_SetupShiftBlueprints_Desc"], molecule.DisplayName),
            MoleculeId = moleculeId,
            AssignedToUserId = assignToUserId,
            Status = SetupTaskStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });

        _db.SetupTasks.AddRange(tasks);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Generated {Count} setup tasks for molecule {MoleculeId}", tasks.Count, moleculeId);

        return tasks.Select(t => MapToDto(t, molecule, null, null)).ToList();
    }

    public async Task<List<SetupTaskDto>> GenerateTasksForCompanyAsync(int companyId, int assignToUserId)
    {
        // SECURITY-AUDITED: SAFE — scoped by specific companyId parameter; admin setup wizard
        var company = await _db.Companies.IgnoreQueryFilters()
            .Include(c => c.Molecule)
                .ThenInclude(m => m!.Area)
            .FirstOrDefaultAsync(c => c.Id == companyId);

        if (company == null || company.Molecule == null)
            return new List<SetupTaskDto>();

        var tasks = new List<SetupTask>();

        // Task 1: Assign BR Director
        tasks.Add(new SetupTask
        {
            Type = SetupTaskType.AssignBRDirector,
            Title = _localizer["SetupTask_AssignBRDirector_Title"],
            Description = string.Format(_localizer["SetupTask_AssignBRDirector_Desc"], company.LocalizedName),
            MoleculeId = company.MoleculeId,
            CompanyId = companyId,
            AssignedToUserId = assignToUserId,
            Status = SetupTaskStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });

        // Task 2: Assign Leads for each job type
        if (company.Molecule.Type == MoleculeType.Workforce)
        {
            // SECURITY-AUDITED: SAFE — scoped by company's moleculeId; admin setup wizard
            var jobTypes = await _jobTypeService.GetJobTypesForMoleculeAsync(company.MoleculeId!.Value);

            foreach (var jobType in jobTypes)
            {
                var taskType = GetLeadTaskType(jobType.Name);
                if (taskType.HasValue)
                {
                    tasks.Add(new SetupTask
                    {
                        Type = taskType.Value,
                        Title = string.Format(_localizer[$"SetupTask_{taskType.Value}_Title"], jobType.DisplayName, company.LocalizedName),
                        Description = string.Format(_localizer[$"SetupTask_{taskType.Value}_Desc"], jobType.DisplayName, company.LocalizedName),
                        MoleculeId = company.MoleculeId,
                        CompanyId = companyId,
                        JobTypeId = jobType.Id,
                        AssignedToUserId = assignToUserId,
                        Status = SetupTaskStatus.Pending,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }
        }

        // Task 3: Assign assigners
        tasks.Add(new SetupTask
        {
            Type = SetupTaskType.AssignAssigners,
            Title = _localizer["SetupTask_AssignAssigners_Title"],
            Description = string.Format(_localizer["SetupTask_AssignAssigners_Desc"], company.LocalizedName),
            MoleculeId = company.MoleculeId,
            CompanyId = companyId,
            AssignedToUserId = assignToUserId,
            Status = SetupTaskStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });

        _db.SetupTasks.AddRange(tasks);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Generated {Count} setup tasks for company {CompanyId}", tasks.Count, companyId);

        return tasks.Select(t => MapToDto(t, company.Molecule, company, null)).ToList();
    }

    public async Task<List<SetupTaskDto>> GetPendingTasksAsync(int userId)
    {
        // SECURITY-AUDITED: SAFE — scoped by specific userId (assigned tasks); admin/owner operation
        var tasks = await _db.SetupTasks.IgnoreQueryFilters()
            .Include(t => t.Molecule)
            .Include(t => t.Company)
            .Include(t => t.JobType)
            .Include(t => t.SuggestedUser)
            .Include(t => t.AssignedToUser)
            .Include(t => t.CompletedByUser)
            .Where(t => t.AssignedToUserId == userId &&
                (t.Status == SetupTaskStatus.Pending || t.Status == SetupTaskStatus.InProgress))
            .OrderBy(t => t.CreatedAt)
            .ToListAsync();

        return tasks.Select(t => MapToDto(t, t.Molecule, t.Company, t.JobType)).ToList();
    }

    public async Task<List<SetupTaskDto>> GetTasksForMoleculeAsync(int moleculeId)
    {
        // SECURITY-AUDITED: SAFE — scoped by specific moleculeId parameter; admin operation
        var tasks = await _db.SetupTasks.IgnoreQueryFilters()
            .Include(t => t.Molecule)
            .Include(t => t.Company)
            .Include(t => t.JobType)
            .Include(t => t.SuggestedUser)
            .Include(t => t.AssignedToUser)
            .Include(t => t.CompletedByUser)
            .Where(t => t.MoleculeId == moleculeId)
            .OrderBy(t => t.Type).ThenBy(t => t.CreatedAt)
            .ToListAsync();

        return tasks.Select(t => MapToDto(t, t.Molecule, t.Company, t.JobType)).ToList();
    }

    public async Task<SetupProgressDto> GetProgressAsync(int moleculeId)
    {
        // SECURITY-AUDITED: SAFE — scoped by specific moleculeId parameter; admin progress view
        var molecule = await _db.Molecules.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == moleculeId);

        if (molecule == null)
        {
            return new SetupProgressDto(moleculeId, "Unknown", 0, 0, 0, 0, 0, 0);
        }

        // SECURITY-AUDITED: SAFE — scoped by specific moleculeId parameter; admin progress view
        var tasks = await _db.SetupTasks.IgnoreQueryFilters()
            .Where(t => t.MoleculeId == moleculeId)
            .ToListAsync();

        var total = tasks.Count;
        var completed = tasks.Count(t => t.Status == SetupTaskStatus.Completed);
        var pending = tasks.Count(t => t.Status == SetupTaskStatus.Pending);
        var skipped = tasks.Count(t => t.Status == SetupTaskStatus.Skipped);
        var inProgress = tasks.Count(t => t.Status == SetupTaskStatus.InProgress);

        var completionPercent = total > 0 ? Math.Round((completed + skipped) * 100.0 / total, 1) : 100;

        return new SetupProgressDto(
            moleculeId,
            molecule.DisplayName,
            total,
            completed,
            pending,
            skipped,
            inProgress,
            completionPercent
        );
    }

    public async Task<bool> CompleteTaskAsync(int taskId, int completedByUserId)
    {
        return await UpdateTaskStatusAsync(taskId, SetupTaskStatus.Completed, completedByUserId);
    }

    public async Task<bool> SkipTaskAsync(int taskId, int skippedByUserId)
    {
        return await UpdateTaskStatusAsync(taskId, SetupTaskStatus.Skipped, skippedByUserId);
    }

    public async Task<bool> UpdateTaskStatusAsync(int taskId, SetupTaskStatus status, int updatedByUserId)
    {
        // SECURITY-AUDITED: SAFE — scoped by specific taskId parameter; admin status update
        var task = await _db.SetupTasks.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == taskId);

        if (task == null)
            return false;

        task.Status = status;

        if (status == SetupTaskStatus.Completed || status == SetupTaskStatus.Skipped)
        {
            task.CompletedAt = DateTime.UtcNow;
            task.CompletedByUserId = updatedByUserId;
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation("Updated setup task {TaskId} to status {Status} by {UserId}",
            taskId, status, updatedByUserId);

        return true;
    }

    private static SetupTaskType? GetDirectorTaskType(string jobTypeName)
    {
        return jobTypeName.ToLower() switch
        {
            "alhut" or "text" => SetupTaskType.AssignDirector,
            _ => null
        };
    }

    private static SetupTaskType? GetLeadTaskType(string jobTypeName)
    {
        return jobTypeName.ToLower() switch
        {
            "alhut" or "text" => SetupTaskType.AssignLead,
            _ => null
        };
    }

    private static SetupTaskDto MapToDto(SetupTask task, Molecule? molecule, Company? company, JobType? jobType)
    {
        return new SetupTaskDto(
            task.Id,
            task.Type,
            task.Title,
            task.Description,
            task.Status,
            task.MoleculeId,
            molecule?.DisplayName,
            task.CompanyId,
            company?.Name,
            task.JobTypeId,
            jobType?.DisplayName,
            task.SuggestedUserId,
            task.SuggestedUser?.DisplayName,
            task.SuggestionReason,
            task.AssignedToUserId,
            task.AssignedToUser?.DisplayName ?? "Unknown",
            task.CreatedAt,
            task.CompletedAt,
            task.CompletedByUser?.DisplayName
        );
    }
}
