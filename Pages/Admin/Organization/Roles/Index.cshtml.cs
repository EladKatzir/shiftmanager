using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Pages.Admin.Organization.Roles;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:AssignRoles policy;
// role template management needs cross-company grant data for configuration
[Authorize(Policy = "Grant:AssignRoles")]
public class IndexModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<IndexModel> _logger;
    private readonly IRoleService _roleService;
    private readonly IAuditLogService _auditLogService;

    public IndexModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        ILogger<IndexModel> logger,
        IRoleService roleService,
        IAuditLogService auditLogService) : base(localizer)
    {
        _db = db;
        _logger = logger;
        _roleService = roleService;
        _auditLogService = auditLogService;
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

        // Load role templates for filter
        var activeTemplates = await _roleService.GetRoleTemplatesAsync();
        AvailableRoles = activeTemplates
            .Select(rt => new RoleTemplateOption(rt.Id, rt.Key, rt.NameKey))
            .ToList();

        if (ViewMode == "assignments")
        {
            // Complex filtered admin query — direct DB access intentional
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
            // Load all role templates with AutoGrants and UserRoles via service
            var templates = await _roleService.GetAllRoleTemplatesWithDetailsAsync();

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
        var assignment = await _roleService.GetUserRoleAssignmentAsync(id);

        if (assignment == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_RoleAssignmentNotFound"].Value;
            return RedirectToPage(new { ViewMode = "assignments" });
        }

        var roleKey = assignment.RoleTemplate.Key;
        var roleNameKey = assignment.RoleTemplate.NameKey;
        var userId = assignment.UserId;
        var userName = assignment.User.DisplayName;

        // Soft delete via service (also removes auto-grants)
        var removeResult = await _roleService.RemoveRoleAsync(id);
        if (!removeResult.Success)
        {
            TempData["ErrorMessage"] = removeResult.ErrorMessage;
            TempData["ErrorId"] = HttpContext.TraceIdentifier;
            return RedirectToPage(new { ViewMode = "assignments" });
        }

        _logger.LogInformation("Revoked role assignment {AssignmentId} ({Role}) from user {UserId}",
            id, roleKey, userId);

        await _auditLogService.LogAsync("RoleRevoked", "UserRoleAssignment", id,
            $"Revoked role '{roleKey}' from user '{userName}' (UserId={userId})");

        TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Success_RoleRevoked"],
            _localizer[roleNameKey], userName);

        return RedirectToPage(new { ViewMode = "assignments", FilterUserId = userId });
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
