using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Resources;

namespace ShiftManager.Pages.Admin.Organization.Projects;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:EditArea policy;
// project management is inherently cross-company hierarchy data
[Authorize(Policy = "Grant:EditArea")]
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

    public record ProjectVM(int Id, string Name, string DisplayName, bool IsActive, int AreaCount, DateTime CreatedAt);

    public List<ProjectVM> Projects { get; set; } = new();

    [BindProperty] public string ProjectName { get; set; } = string.Empty;
    [BindProperty] public string ProjectDisplayName { get; set; } = string.Empty;

    public async Task OnGetAsync()
    {

        Projects = await _db.Projects
            .IgnoreQueryFilters()
            .OrderBy(p => p.Name)
            .Select(p => new ProjectVM(p.Id, p.Name, p.DisplayName, p.IsActive, p.Areas.Count(a => a.IsActive), p.CreatedAt))
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (string.IsNullOrWhiteSpace(ProjectName))
        {
            TempData["ErrorMessage"] = _localizer["Error_ProjectNameRequired"].Value;
            return RedirectToPage();
        }

        var project = new Project
        {
            Name = ProjectName.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(ProjectDisplayName) ? ProjectName.Trim() : ProjectDisplayName.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Created Project {ProjectId}: {ProjectName}", project.Id, project.Name);
        TempData["SuccessMessage"] = string.Format(_localizer["Success_ProjectCreated"], project.DisplayName);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(int id)
    {
        var project = await _db.Projects.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id);
        if (project == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_ProjectNotFound"].Value;
            return RedirectToPage();
        }

        project.IsActive = !project.IsActive;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Project {ProjectId} active status changed to {IsActive}", id, project.IsActive);
        TempData["SuccessMessage"] = project.IsActive
            ? string.Format(_localizer["Success_ProjectActivated"], project.DisplayName)
            : string.Format(_localizer["Success_ProjectDeactivated"], project.DisplayName);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var project = await _db.Projects.Include(p => p.Areas).FirstOrDefaultAsync(p => p.Id == id);
        if (project == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_ProjectNotFound"].Value;
            return RedirectToPage();
        }

        if (project.Areas.Any())
        {
            TempData["ErrorMessage"] = _localizer["Error_CannotDeleteProjectWithAreas"].Value;
            return RedirectToPage();
        }

        _db.Projects.Remove(project);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Deleted Project {ProjectId}: {ProjectName}", id, project.Name);
        TempData["SuccessMessage"] = string.Format(_localizer["Success_ProjectDeleted"], project.DisplayName);
        return RedirectToPage();
    }
}
