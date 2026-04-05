using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Helpers;
using ShiftManager.Models;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Pages.Admin.Organization.Projects;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:EditArea policy;
// project management is inherently cross-company hierarchy data
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

    public record ProjectVM(int Id, string Name, string DisplayName, bool IsActive, int AreaCount, DateTime CreatedAt);

    public List<ProjectVM> Projects { get; set; } = new();

    [BindProperty] public string ProjectName { get; set; } = string.Empty;
    [BindProperty] public string ProjectDisplayName { get; set; } = string.Empty;

    [BindProperty] public int EditId { get; set; }
    [BindProperty] public string EditName { get; set; } = string.Empty;
    [BindProperty] public string EditDisplayName { get; set; } = string.Empty;

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

        if (InputSanitizer.ContainsDangerousContent(ProjectName) || InputSanitizer.ContainsDangerousContent(ProjectDisplayName))
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidInput"].Value;
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

        await _auditLogService.LogAsync("ProjectCreated", "Project", project.Id,
            $"Created project '{project.DisplayName}'");

        TempData["SuccessMessage"] = string.Format(_localizer["Success_ProjectCreated"], project.DisplayName);
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
        var project = await _db.Projects.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == EditId);
        if (project == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_ProjectNotFound"].Value;
            return RedirectToPage();
        }

        var oldName = project.Name;
        project.Name = EditName.Trim();
        project.DisplayName = string.IsNullOrWhiteSpace(EditDisplayName) ? EditName.Trim() : EditDisplayName.Trim();
        await _db.SaveChangesAsync();

        _logger.LogInformation("Project {ProjectId} renamed from '{OldName}' to '{NewName}'", EditId, oldName, project.Name);

        await _auditLogService.LogAsync("ProjectRenamed", "Project", EditId,
            $"Project renamed from '{oldName}' to '{project.DisplayName}'");

        TempData["SuccessMessage"] = string.Format(_localizer["Success_ProjectRenamed"], project.DisplayName);
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

        await _auditLogService.LogAsync("ProjectUpdated", "Project", id,
            $"Project '{project.DisplayName}' {(project.IsActive ? "activated" : "deactivated")}");

        TempData["SuccessMessage"] = project.IsActive
            ? string.Format(_localizer["Success_ProjectActivated"], project.DisplayName)
            : string.Format(_localizer["Success_ProjectDeactivated"], project.DisplayName);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        // SECURITY-AUDITED: SAFE — requires Grant:EditArea policy; consistent with GET handler
        var project = await _db.Projects.IgnoreQueryFilters().Include(p => p.Areas).FirstOrDefaultAsync(p => p.Id == id);
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

        await _auditLogService.LogAsync("ProjectDeleted", "Project", id,
            $"Deleted project '{project.DisplayName}'");

        TempData["SuccessMessage"] = string.Format(_localizer["Success_ProjectDeleted"], project.DisplayName);
        return RedirectToPage();
    }
}
