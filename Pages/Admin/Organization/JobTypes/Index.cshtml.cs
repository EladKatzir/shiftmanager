using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Helpers;
using ShiftManager.Models;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Pages.Admin.Organization.JobTypes;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:ManageJobTypes policy;
// job type management is inherently cross-company hierarchy data
[Authorize(Policy = "Grant:ManageJobTypes")]
public class IndexModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<IndexModel> _logger;
    private readonly IJobTypeService _jobTypeService;
    private readonly IAuditLogService _auditLogService;

    public IndexModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        ILogger<IndexModel> logger,
        IJobTypeService jobTypeService,
        IAuditLogService auditLogService) : base(localizer)
    {
        _db = db;
        _logger = logger;
        _jobTypeService = jobTypeService;
        _auditLogService = auditLogService;
    }

    // View Models
    public record JobTypeVM(int Id, string Name, string DisplayName, string? Color, int SortOrder, string AreaName, string ProjectName, string? MoleculeName, bool IsActive, int UserCount);
    public record AreaOption(int Id, string Name, string ProjectName);
    public record MoleculeOption(int Id, string Name, int AreaId, string AreaName);

    // Data
    public List<JobTypeVM> JobTypes { get; set; } = new();
    public List<AreaOption> AvailableAreas { get; set; } = new();
    public List<MoleculeOption> AvailableMolecules { get; set; } = new();

    // Form Bindings
    [BindProperty] public string JobTypeName { get; set; } = string.Empty;
    [BindProperty] public string JobTypeDisplayName { get; set; } = string.Empty;
    [BindProperty] public string? JobTypeColor { get; set; }
    [BindProperty] public int SelectedAreaId { get; set; }
    [BindProperty] public int? SelectedMoleculeId { get; set; }
    [BindProperty] public int SortOrder { get; set; } = 0;

    [BindProperty] public int EditId { get; set; }
    [BindProperty] public string EditName { get; set; } = string.Empty;
    [BindProperty] public string EditDisplayName { get; set; } = string.Empty;
    [BindProperty] public string? EditColor { get; set; }
    [BindProperty] public int EditSortOrder { get; set; }

    public async Task OnGetAsync()
    {

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

        var allJobTypes = await _jobTypeService.GetAllJobTypesWithHierarchyAsync();
        JobTypes = allJobTypes
            .Select(jt => new JobTypeVM(
                jt.Id,
                jt.Name,
                jt.DisplayName,
                jt.Color,
                jt.SortOrder,
                jt.Area.DisplayName,
                jt.Area.Project.DisplayName,
                jt.Molecule?.DisplayName,
                jt.IsActive,
                userCountsByJobType.GetValueOrDefault(jt.Id, 0)
            ))
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

        AvailableMolecules = (await _db.Molecules
            .IgnoreQueryFilters()
            .Where(m => m.IsActive)
            .Include(m => m.Area)
            .Select(m => new MoleculeOption(m.Id, m.DisplayName, m.AreaId, m.Area.DisplayName))
            .ToListAsync())
            .OrderBy(m => m.AreaName)
            .ThenBy(m => m.Name)
            .ToList();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (string.IsNullOrWhiteSpace(JobTypeName))
        {
            TempData["ErrorMessage"] = _localizer["Error_JobTypeNameRequired"].Value;
            return RedirectToPage();
        }

        if (InputSanitizer.ContainsDangerousContent(JobTypeName) || InputSanitizer.ContainsDangerousContent(JobTypeDisplayName))
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidInput"].Value;
            return RedirectToPage();
        }

        if (SelectedAreaId <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_AreaRequired"].Value;
            return RedirectToPage();
        }

        var area = await _db.Areas.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == SelectedAreaId);
        if (area == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_AreaNotFound"].Value;
            return RedirectToPage();
        }

        // Validate color format
        if (!string.IsNullOrWhiteSpace(JobTypeColor) && !Regex.IsMatch(JobTypeColor.Trim(), @"^#[0-9A-Fa-f]{6}$"))
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidColor"].Value;
            return RedirectToPage();
        }

        // Validate molecule belongs to selected area (if specified)
        if (SelectedMoleculeId.HasValue)
        {
            var molecule = await _db.Molecules.IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.Id == SelectedMoleculeId.Value);
            if (molecule == null || molecule.AreaId != SelectedAreaId)
            {
                TempData["ErrorMessage"] = _localizer["Error_MoleculeMustBelongToArea"].Value;
                return RedirectToPage();
            }
        }

        var createResult = await _jobTypeService.CreateJobTypeAsync(
            JobTypeName.Trim(),
            SelectedAreaId,
            TimeOnly.MinValue,
            TimeOnly.MinValue,
            isActive: true);

        if (!createResult.Success)
        {
            TempData["ErrorMessage"] = createResult.ErrorMessage;
            TempData["ErrorId"] = HttpContext.TraceIdentifier;
            return RedirectToPage();
        }

        var jobType = createResult.Value!;
        // Update additional properties not covered by CreateJobTypeAsync
        jobType.DisplayName = string.IsNullOrWhiteSpace(JobTypeDisplayName) ? JobTypeName.Trim() : JobTypeDisplayName.Trim();
        jobType.Color = string.IsNullOrWhiteSpace(JobTypeColor) ? null : JobTypeColor.Trim();
        jobType.SortOrder = SortOrder;
        jobType.MoleculeId = SelectedMoleculeId;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Created JobType {JobTypeId}: {JobTypeName} in Area {AreaId}",
            jobType.Id, jobType.Name, jobType.AreaId);

        await _auditLogService.LogAsync("JobTypeCreated", "JobType", jobType.Id,
            $"Created job type '{jobType.DisplayName}' in area (AreaId={jobType.AreaId})");

        TempData["SuccessMessage"] = string.Format(_localizer["Success_JobTypeCreated"], jobType.DisplayName);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEditAsync()
    {
        if (EditId <= 0 || string.IsNullOrWhiteSpace(EditName))
        {
            TempData["ErrorMessage"] = _localizer["Error_RequiredFields"].Value;
            return RedirectToPage();
        }

        if (InputSanitizer.ContainsDangerousContent(EditName) || InputSanitizer.ContainsDangerousContent(EditDisplayName))
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidInput"].Value;
            return RedirectToPage();
        }

        if (!string.IsNullOrWhiteSpace(EditColor) && !Regex.IsMatch(EditColor.Trim(), @"^#[0-9A-Fa-f]{6}$"))
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidColor"].Value;
            return RedirectToPage();
        }

        // SECURITY-AUDITED: SAFE — requires Grant:ManageJobTypes policy; consistent with GET handler
        var jobType = await _db.JobTypes.IgnoreQueryFilters().FirstOrDefaultAsync(jt => jt.Id == EditId);
        if (jobType == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_JobTypeNotFound"].Value;
            return RedirectToPage();
        }

        var oldName = jobType.Name;
        jobType.Name = EditName.Trim();
        jobType.DisplayName = string.IsNullOrWhiteSpace(EditDisplayName) ? EditName.Trim() : EditDisplayName.Trim();
        jobType.Color = string.IsNullOrWhiteSpace(EditColor) ? null : EditColor.Trim();
        jobType.SortOrder = EditSortOrder;
        await _db.SaveChangesAsync();

        _logger.LogInformation("JobType {JobTypeId} edited: name '{OldName}' -> '{NewName}'", EditId, oldName, jobType.Name);

        await _auditLogService.LogAsync("JobTypeEdited", "JobType", EditId,
            $"Job type edited: '{oldName}' -> '{jobType.DisplayName}'");

        TempData["SuccessMessage"] = string.Format(_localizer["Success_JobTypeEdited"], jobType.DisplayName);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(int id)
    {
        // Get the job type info for logging/messages before toggling
        // (GetAllJobTypesWithAreaAsync includes inactive, so we can find it)
        var allJobTypes = await _jobTypeService.GetAllJobTypesWithAreaAsync();
        var jobTypeInfo = allJobTypes.FirstOrDefault(jt => jt.Id == id);
        if (jobTypeInfo == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_JobTypeNotFound"].Value;
            return RedirectToPage();
        }

        var toggleResult = await _jobTypeService.ToggleActiveAsync(id);
        if (!toggleResult.Success)
        {
            TempData["ErrorMessage"] = toggleResult.ErrorMessage ?? _localizer["Error_JobTypeNotFound"].Value;
            TempData["ErrorId"] = HttpContext.TraceIdentifier;
            return RedirectToPage();
        }

        var newIsActive = !jobTypeInfo.IsActive; // toggled state
        _logger.LogInformation("JobType {JobTypeId} ({JobTypeName}) active status changed to {IsActive}",
            id, jobTypeInfo.Name, newIsActive);

        await _auditLogService.LogAsync("JobTypeUpdated", "JobType", id,
            $"Job type '{jobTypeInfo.DisplayName}' {(newIsActive ? "activated" : "deactivated")}");

        TempData["SuccessMessage"] = newIsActive
            ? string.Format(_localizer["Success_JobTypeActivated"], jobTypeInfo.DisplayName)
            : string.Format(_localizer["Success_JobTypeDeactivated"], jobTypeInfo.DisplayName);

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var userCount = await _db.Users
            .IgnoreQueryFilters()
            .CountAsync(u => u.JobTypeId == id);

        if (userCount > 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_CannotDeleteJobTypeWithUsers"].Value;
            return RedirectToPage();
        }

        // Get info for logging before deletion
        var allJobTypes = await _jobTypeService.GetAllJobTypesWithAreaAsync();
        var jobTypeInfo = allJobTypes.FirstOrDefault(jt => jt.Id == id);

        var deleteResult = await _jobTypeService.DeleteJobTypeAsync(id);
        if (!deleteResult.Success)
        {
            TempData["ErrorMessage"] = deleteResult.ErrorMessage ?? _localizer["Error_JobTypeNotFound"].Value;
            TempData["ErrorId"] = HttpContext.TraceIdentifier;
            return RedirectToPage();
        }

        _logger.LogInformation("Deleted JobType {JobTypeId}: {JobTypeName}", id, jobTypeInfo?.Name ?? "Unknown");

        await _auditLogService.LogAsync("JobTypeDeleted", "JobType", id,
            $"Deleted job type '{jobTypeInfo?.DisplayName ?? "Unknown"}'");

        TempData["SuccessMessage"] = string.Format(_localizer["Success_JobTypeDeleted"], jobTypeInfo?.DisplayName ?? "Unknown");
        return RedirectToPage();
    }
}
