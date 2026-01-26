using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Resources;

namespace ShiftManager.Pages.Admin.Organization.Areas;

[Authorize(Policy = "IsAdmin")]
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

    public record AreaVM(int Id, string Name, string DisplayName, string ProjectName, bool IsActive, int MoleculeCount, int JobTypeCount);
    public record ProjectOption(int Id, string Name);

    public List<AreaVM> Areas { get; set; } = new();
    public List<ProjectOption> AvailableProjects { get; set; } = new();

    [BindProperty] public string AreaName { get; set; } = string.Empty;
    [BindProperty] public string AreaDisplayName { get; set; } = string.Empty;
    [BindProperty] public int SelectedProjectId { get; set; }

    public async Task OnGetAsync()
    {
        if (TempData["SuccessMessage"] is string successMsg) Success = successMsg;
        if (TempData["ErrorMessage"] is string errorMsg) Error = errorMsg;

        Areas = await _db.Areas
            .IgnoreQueryFilters()
            .Include(a => a.Project)
            .Select(a => new AreaVM(a.Id, a.Name, a.DisplayName, a.Project.DisplayName, a.IsActive, a.Molecules.Count(m => m.IsActive), a.JobTypes.Count(jt => jt.IsActive)))
            .OrderBy(a => a.ProjectName).ThenBy(a => a.Name)
            .ToListAsync();

        AvailableProjects = await _db.Projects
            .IgnoreQueryFilters()
            .Where(p => p.IsActive)
            .Select(p => new ProjectOption(p.Id, p.DisplayName))
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (string.IsNullOrWhiteSpace(AreaName))
        {
            TempData["ErrorMessage"] = _localizer["Error_AreaNameRequired"];
            return RedirectToPage();
        }
        if (SelectedProjectId <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_ProjectRequired"];
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
        TempData["SuccessMessage"] = string.Format(_localizer["Success_AreaCreated"], area.DisplayName);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(int id)
    {
        var area = await _db.Areas.FindAsync(id);
        if (area == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_AreaNotFound"];
            return RedirectToPage();
        }

        area.IsActive = !area.IsActive;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Area {AreaId} active status changed to {IsActive}", id, area.IsActive);
        TempData["SuccessMessage"] = area.IsActive
            ? string.Format(_localizer["Success_AreaActivated"], area.DisplayName)
            : string.Format(_localizer["Success_AreaDeactivated"], area.DisplayName);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var area = await _db.Areas.Include(a => a.Molecules).Include(a => a.JobTypes).FirstOrDefaultAsync(a => a.Id == id);
        if (area == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_AreaNotFound"];
            return RedirectToPage();
        }

        if (area.Molecules.Any() || area.JobTypes.Any())
        {
            TempData["ErrorMessage"] = _localizer["Error_CannotDeleteAreaWithDependencies"];
            return RedirectToPage();
        }

        _db.Areas.Remove(area);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Deleted Area {AreaId}: {AreaName}", id, area.Name);
        TempData["SuccessMessage"] = string.Format(_localizer["Success_AreaDeleted"], area.DisplayName);
        return RedirectToPage();
    }
}
