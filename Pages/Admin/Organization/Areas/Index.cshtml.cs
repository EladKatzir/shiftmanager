using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Resources;
using ShiftManager.Helpers;
using ShiftManager.Services;

namespace ShiftManager.Pages.Admin.Organization.Areas;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:EditArea policy;
// area management is inherently cross-company hierarchy data
[Authorize(Policy = "Grant:EditArea")]
public class IndexModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<IndexModel> _logger;
    private readonly IAuditLogService _auditLogService;

    public IndexModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        ILogger<IndexModel> logger,
        IAuditLogService auditLogService) : base(localizer)
    {
        _db = db;
        _logger = logger;
        _auditLogService = auditLogService;
    }

    public record AreaVM(int Id, string Name, string DisplayName, string ProjectName, bool IsActive, int MoleculeCount, int JobTypeCount);
    public record ProjectOption(int Id, string Name);

    public List<AreaVM> Areas { get; set; } = new();
    public List<ProjectOption> AvailableProjects { get; set; } = new();

    [BindProperty] public string AreaName { get; set; } = string.Empty;
    [BindProperty] public string AreaDisplayName { get; set; } = string.Empty;
    [BindProperty] public int SelectedProjectId { get; set; }

    [BindProperty] public int EditId { get; set; }
    [BindProperty] public string EditName { get; set; } = string.Empty;
    [BindProperty] public string EditDisplayName { get; set; } = string.Empty;

    public async Task OnGetAsync()
    {

        Areas = await _db.Areas
            .IgnoreQueryFilters()
            .Include(a => a.Project)
            .OrderBy(a => a.Project.DisplayName).ThenBy(a => a.Name)
            .Select(a => new AreaVM(a.Id, a.Name, a.DisplayName, a.Project.DisplayName, a.IsActive, a.Molecules.Count(m => m.IsActive), a.JobTypes.Count(jt => jt.IsActive)))
            .ToListAsync();

        AvailableProjects = await _db.Projects
            .IgnoreQueryFilters()
            .Where(p => p.IsActive)
            .OrderBy(p => p.DisplayName)
            .Select(p => new ProjectOption(p.Id, p.DisplayName))
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (string.IsNullOrWhiteSpace(AreaName))
        {
            TempData["ErrorMessage"] = _localizer["Error_AreaNameRequired"].Value;
            return RedirectToPage();
        }

        if (InputSanitizer.ContainsDangerousContent(AreaName) || InputSanitizer.ContainsDangerousContent(AreaDisplayName))
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidInput"].Value;
            return RedirectToPage();
        }

        if (SelectedProjectId <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_ProjectRequired"].Value;
            return RedirectToPage();
        }

        var area = new Area
        {
            ProjectId = SelectedProjectId,
            Name = AreaName.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(AreaDisplayName) ? AreaName.Trim() : AreaDisplayName.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.Areas.Add(area);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Created Area {AreaId}: {AreaName} in Project {ProjectId}", area.Id, area.Name, area.ProjectId);

        await _auditLogService.LogAsync("AreaCreated", "Area", area.Id,
            $"Created area '{area.DisplayName}' in project (ProjectId={area.ProjectId})");

        TempData["SuccessMessage"] = string.Format(_localizer["Success_AreaCreated"], area.DisplayName);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRenameAsync()
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

        // SECURITY-AUDITED: SAFE — requires Grant:EditArea policy; consistent with GET handler
        var area = await _db.Areas.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == EditId);
        if (area == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_AreaNotFound"].Value;
            return RedirectToPage();
        }

        var oldName = area.Name;
        area.Name = EditName.Trim();
        area.DisplayName = string.IsNullOrWhiteSpace(EditDisplayName) ? EditName.Trim() : EditDisplayName.Trim();
        await _db.SaveChangesAsync();

        _logger.LogInformation("Area {AreaId} renamed from '{OldName}' to '{NewName}'", EditId, oldName, area.Name);

        await _auditLogService.LogAsync("AreaRenamed", "Area", EditId,
            $"Area renamed from '{oldName}' to '{area.DisplayName}'");

        TempData["SuccessMessage"] = string.Format(_localizer["Success_AreaRenamed"], area.DisplayName);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(int id)
    {
        // SECURITY-AUDITED: SAFE — requires Grant:EditArea policy; consistent with GET handler
        var area = await _db.Areas.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == id);
        if (area == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_AreaNotFound"].Value;
            return RedirectToPage();
        }

        area.IsActive = !area.IsActive;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Area {AreaId} active status changed to {IsActive}", id, area.IsActive);

        await _auditLogService.LogAsync("AreaUpdated", "Area", id,
            $"Area '{area.DisplayName}' {(area.IsActive ? "activated" : "deactivated")}");

        TempData["SuccessMessage"] = area.IsActive
            ? string.Format(_localizer["Success_AreaActivated"], area.DisplayName)
            : string.Format(_localizer["Success_AreaDeactivated"], area.DisplayName);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        // SECURITY-AUDITED: SAFE — requires Grant:EditArea policy; consistent with GET handler
        var area = await _db.Areas.IgnoreQueryFilters().Include(a => a.Molecules).Include(a => a.JobTypes).FirstOrDefaultAsync(a => a.Id == id);
        if (area == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_AreaNotFound"].Value;
            return RedirectToPage();
        }

        if (area.Molecules.Any() || area.JobTypes.Any())
        {
            TempData["ErrorMessage"] = _localizer["Error_CannotDeleteAreaWithDependencies"].Value;
            return RedirectToPage();
        }

        _db.Areas.Remove(area);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Deleted Area {AreaId}: {AreaName}", id, area.Name);

        await _auditLogService.LogAsync("AreaDeleted", "Area", id,
            $"Deleted area '{area.DisplayName}'");

        TempData["SuccessMessage"] = string.Format(_localizer["Success_AreaDeleted"], area.DisplayName);
        return RedirectToPage();
    }
}
