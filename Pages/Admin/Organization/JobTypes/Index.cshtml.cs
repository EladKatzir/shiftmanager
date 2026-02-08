using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Resources;

namespace ShiftManager.Pages.Admin.Organization.JobTypes;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:ManageJobTypes policy;
// job type management is inherently cross-company hierarchy data
[Authorize(Policy = "Grant:ManageJobTypes")]
public class IndexModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        ILogger<IndexModel> logger) : base(localizer)
    {
        _db = db;
        _logger = logger;
    }

    // View Models
    public record JobTypeVM(int Id, string Name, string DisplayName, string? Color, int SortOrder, string AreaName, string ProjectName, bool IsActive, int UserCount);
    public record AreaOption(int Id, string Name, string ProjectName);

    // Data
    public List<JobTypeVM> JobTypes { get; set; } = new();
    public List<AreaOption> AvailableAreas { get; set; } = new();

    // Form Bindings
    [BindProperty] public string JobTypeName { get; set; } = string.Empty;
    [BindProperty] public string JobTypeDisplayName { get; set; } = string.Empty;
    [BindProperty] public string? JobTypeColor { get; set; }
    [BindProperty] public int SelectedAreaId { get; set; }
    [BindProperty] public int SortOrder { get; set; } = 0;

    public async Task OnGetAsync()
    {
        if (TempData["SuccessMessage"] is string successMsg)
            Success = successMsg;
        if (TempData["ErrorMessage"] is string errorMsg)
            Error = errorMsg;

        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        var userCountsByJobType = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.IsActive && u.JobTypeId.HasValue)
            .GroupBy(u => u.JobTypeId!.Value)
            .Select(g => new { JobTypeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.JobTypeId, x => x.Count);

        JobTypes = (await _db.JobTypes
            .IgnoreQueryFilters()
            .Include(jt => jt.Area)
            .ThenInclude(a => a.Project)
            .Select(jt => new JobTypeVM(
                jt.Id,
                jt.Name,
                jt.DisplayName,
                jt.Color,
                jt.SortOrder,
                jt.Area.DisplayName,
                jt.Area.Project.DisplayName,
                jt.IsActive,
                userCountsByJobType.GetValueOrDefault(jt.Id, 0)
            ))
            .ToListAsync())
            .OrderBy(jt => jt.ProjectName)
            .ThenBy(jt => jt.AreaName)
            .ThenBy(jt => jt.SortOrder)
            .ThenBy(jt => jt.Name)
            .ToList();

        AvailableAreas = (await _db.Areas
            .IgnoreQueryFilters()
            .Where(a => a.IsActive)
            .Include(a => a.Project)
            .Where(a => a.Project.IsActive)
            .Select(a => new AreaOption(a.Id, a.DisplayName, a.Project.DisplayName))
            .ToListAsync())
            .OrderBy(a => a.ProjectName)
            .ThenBy(a => a.Name)
            .ToList();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (string.IsNullOrWhiteSpace(JobTypeName))
        {
            TempData["ErrorMessage"] = _localizer["Error_JobTypeNameRequired"];
            return RedirectToPage();
        }

        if (SelectedAreaId <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_AreaRequired"];
            return RedirectToPage();
        }

        var area = await _db.Areas.FindAsync(SelectedAreaId);
        if (area == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_AreaNotFound"];
            return RedirectToPage();
        }

        var jobType = new JobType
        {
            AreaId = SelectedAreaId,
            Name = JobTypeName.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(JobTypeDisplayName) ? JobTypeName.Trim() : JobTypeDisplayName.Trim(),
            Color = string.IsNullOrWhiteSpace(JobTypeColor) ? null : JobTypeColor.Trim(),
            SortOrder = SortOrder,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.JobTypes.Add(jobType);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Created JobType {JobTypeId}: {JobTypeName} in Area {AreaId}",
            jobType.Id, jobType.Name, jobType.AreaId);

        TempData["SuccessMessage"] = string.Format(_localizer["Success_JobTypeCreated"], jobType.DisplayName);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(int id)
    {
        var jobType = await _db.JobTypes.FindAsync(id);
        if (jobType == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_JobTypeNotFound"];
            return RedirectToPage();
        }

        jobType.IsActive = !jobType.IsActive;
        await _db.SaveChangesAsync();

        _logger.LogInformation("JobType {JobTypeId} ({JobTypeName}) active status changed to {IsActive}",
            id, jobType.Name, jobType.IsActive);

        TempData["SuccessMessage"] = jobType.IsActive
            ? string.Format(_localizer["Success_JobTypeActivated"], jobType.DisplayName)
            : string.Format(_localizer["Success_JobTypeDeactivated"], jobType.DisplayName);

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var userCount = await _db.Users
            .IgnoreQueryFilters()
            .CountAsync(u => u.JobTypeId == id);

        if (userCount > 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_CannotDeleteJobTypeWithUsers"];
            return RedirectToPage();
        }

        var jobType = await _db.JobTypes.FindAsync(id);
        if (jobType == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_JobTypeNotFound"];
            return RedirectToPage();
        }

        _db.JobTypes.Remove(jobType);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Deleted JobType {JobTypeId}: {JobTypeName}", id, jobType.Name);

        TempData["SuccessMessage"] = string.Format(_localizer["Success_JobTypeDeleted"], jobType.DisplayName);
        return RedirectToPage();
    }
}
