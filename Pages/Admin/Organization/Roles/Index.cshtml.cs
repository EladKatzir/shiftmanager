using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;

namespace ShiftManager.Pages.Admin.Organization.Roles;

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

    public record RoleTemplateVM(
        int Id,
        string Key,
        string NameKey,
        string DescriptionKey,
        RoleScopeLevel ScopeLevel,
        bool IsSystem,
        bool IsActive,
        int AssignmentCount,
        int GrantCount);

    public record UserRoleVM(
        int Id,
        string UserName,
        int UserId,
        string RoleNameKey,
        string ScopeDescription,
        DateTime AssignedAt,
        string? AssignedByName,
        bool IsActive);

    public record RoleTemplateOption(int Id, string Key, string NameKey);

    public List<RoleTemplateVM> RoleTemplates { get; set; } = new();
    public List<UserRoleVM> UserRoles { get; set; } = new();
    public List<RoleTemplateOption> AvailableRoles { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int? FilterRoleId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? FilterUserId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string ViewMode { get; set; } = "roles"; // "roles" or "assignments"

    public async Task OnGetAsync()
    {
        if (TempData["SuccessMessage"] is string successMsg) Success = successMsg;
        if (TempData["ErrorMessage"] is string errorMsg) Error = errorMsg;

        // Load role templates for filter
        AvailableRoles = await _db.RoleTemplates
            .IgnoreQueryFilters()
            .Where(rt => rt.IsActive)
            .OrderBy(rt => rt.SortOrder).ThenBy(rt => rt.Key)
            .Select(rt => new RoleTemplateOption(rt.Id, rt.Key, rt.NameKey))
            .ToListAsync();

        if (ViewMode == "assignments")
        {
            // Show user role assignments
            var assignmentsQuery = _db.UserRoleAssignments
                .IgnoreQueryFilters()
                .Include(ura => ura.User)
                .Include(ura => ura.RoleTemplate)
                .Include(ura => ura.AssignedByUser)
                .Include(ura => ura.Company)
                .Include(ura => ura.Department)
                .Include(ura => ura.Molecule)
                .Include(ura => ura.Area)
                .Include(ura => ura.JobType)
                .AsQueryable();

            if (FilterRoleId.HasValue)
            {
                assignmentsQuery = assignmentsQuery.Where(ura => ura.RoleTemplateId == FilterRoleId.Value);
            }

            if (FilterUserId.HasValue)
            {
                assignmentsQuery = assignmentsQuery.Where(ura => ura.UserId == FilterUserId.Value);
            }

            var assignmentsData = await assignmentsQuery
                .OrderBy(ura => ura.User.DisplayName).ThenBy(ura => ura.RoleTemplate.Key)
                .ToListAsync();

            UserRoles = assignmentsData.Select(ura => new UserRoleVM(
                ura.Id,
                ura.User.DisplayName,
                ura.UserId,
                ura.RoleTemplate.NameKey,
                BuildScopeDescription(ura),
                ura.AssignedAt,
                ura.AssignedByUser?.DisplayName,
                ura.IsActive
            )).ToList();
        }
        else
        {
            // Show role templates (default view)
            var templates = await _db.RoleTemplates
                .IgnoreQueryFilters()
                .Include(rt => rt.AutoGrants)
                .Include(rt => rt.UserRoles)
                .OrderBy(rt => rt.SortOrder).ThenBy(rt => rt.Key)
                .ToListAsync();

            RoleTemplates = templates.Select(rt => new RoleTemplateVM(
                rt.Id,
                rt.Key,
                rt.NameKey,
                rt.DescriptionKey,
                rt.ScopeLevel,
                rt.IsSystem,
                rt.IsActive,
                rt.UserRoles.Count(ur => ur.IsActive),
                rt.AutoGrants.Count
            )).ToList();
        }
    }

    public async Task<IActionResult> OnPostRevokeAsync(int id)
    {
        var assignment = await _db.UserRoleAssignments
            .IgnoreQueryFilters()
            .Include(ura => ura.User)
            .Include(ura => ura.RoleTemplate)
            .FirstOrDefaultAsync(ura => ura.Id == id);

        if (assignment == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_RoleAssignmentNotFound"];
            return RedirectToPage(new { ViewMode = "assignments" });
        }

        // Soft delete - mark as inactive
        assignment.IsActive = false;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Revoked role assignment {AssignmentId} ({Role}) from user {UserId}",
            id, assignment.RoleTemplate.Key, assignment.UserId);

        TempData["SuccessMessage"] = string.Format(_localizer["Success_RoleRevoked"],
            _localizer[assignment.RoleTemplate.NameKey], assignment.User.DisplayName);

        return RedirectToPage(new { ViewMode = "assignments", FilterUserId = assignment.UserId });
    }

    private static string BuildScopeDescription(UserRoleAssignment ura)
    {
        var parts = new List<string>();

        if (ura.Area != null) parts.Add($"Area: {ura.Area.DisplayName}");
        if (ura.Molecule != null) parts.Add($"Molecule: {ura.Molecule.DisplayName}");
        if (ura.Company != null) parts.Add($"Company: {ura.Company.Name}");
        if (ura.Department != null) parts.Add($"Dept: {ura.Department.DisplayName}");
        if (ura.JobType != null) parts.Add($"JobType: {ura.JobType.DisplayName}");

        return parts.Any() ? string.Join(", ", parts) : "Global";
    }
}
