using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.Admin.Organization.Roles;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:AssignRoles policy;
// role assignment needs cross-company hierarchy references for scope selection
[Authorize(Policy = "Grant:AssignRoles")]
public class AssignModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<AssignModel> _logger;
    private readonly IRoleService _roleService;
    private readonly IJobTypeService _jobTypeService;
    private readonly IAuditLogService _auditLogService;

    public AssignModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        ILogger<AssignModel> logger,
        IRoleService roleService,
        IJobTypeService jobTypeService,
        IAuditLogService auditLogService) : base(localizer)
    {
        _db = db;
        _logger = logger;
        _roleService = roleService;
        _jobTypeService = jobTypeService;
        _auditLogService = auditLogService;
    }

    public record UserOption(int Id, string DisplayName, string Email);
    public record RoleTemplateOption(int Id, string Key, string NameKey, RoleScopeLevel ScopeLevel, string? DescriptionKey);
    public record ScopeOption(int Id, string Name, string Type);

    public List<UserOption> AvailableUsers { get; set; } = new();
    public List<RoleTemplateOption> AvailableRoles { get; set; } = new();
    public List<ScopeOption> AvailableProjects { get; set; } = new();
    public List<ScopeOption> AvailableAreas { get; set; } = new();
    public List<ScopeOption> AvailableMolecules { get; set; } = new();
    public List<ScopeOption> AvailableCompanies { get; set; } = new();
    public List<ScopeOption> AvailableDepartments { get; set; } = new();
    public List<ScopeOption> AvailableJobTypes { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int? UserId { get; set; }

    [BindProperty]
    public int SelectedUserId { get; set; }

    [BindProperty]
    public int SelectedRoleId { get; set; }

    [BindProperty]
    public int? ScopeAreaId { get; set; }

    [BindProperty]
    public int? ScopeMoleculeId { get; set; }

    [BindProperty]
    public int? ScopeCompanyId { get; set; }

    [BindProperty]
    public int? ScopeDepartmentId { get; set; }

    [BindProperty]
    public int? ScopeJobTypeId { get; set; }

    public string? SelectedUserName { get; set; }

    public async Task OnGetAsync()
    {

        await LoadDropdownOptionsAsync();

        if (UserId.HasValue)
        {
            SelectedUserId = UserId.Value;
            var user = await _db.Users.FindAsync(UserId.Value);
            if (user != null)
            {
                SelectedUserName = user.DisplayName;
            }
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (SelectedUserId <= 0)
        {
            Error = _localizer["Error_UserRequired"];
            await LoadDropdownOptionsAsync();
            return Page();
        }

        if (SelectedRoleId <= 0)
        {
            Error = _localizer["Error_RoleRequired"];
            await LoadDropdownOptionsAsync();
            return Page();
        }

        // Verify user exists
        var user = await _db.Users.FindAsync(SelectedUserId);
        if (user == null)
        {
            Error = _localizer["Error_UserNotFound"];
            await LoadDropdownOptionsAsync();
            return Page();
        }

        // Verify role template exists
        var roleTemplate = await _roleService.GetRoleTemplateAsync(SelectedRoleId);
        if (roleTemplate == null)
        {
            Error = _localizer["Error_RoleNotFound"];
            await LoadDropdownOptionsAsync();
            return Page();
        }

        // Validate scope based on role's ScopeLevel
        var scopeError = ValidateScope(roleTemplate.ScopeLevel);
        if (scopeError != null)
        {
            Error = scopeError;
            await LoadDropdownOptionsAsync();
            return Page();
        }

        // Get current user ID for audit
        var currentUserIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        int.TryParse(currentUserIdClaim, out var currentUserId);

        // Assign role via service (handles duplicate checks and auto-grants internally)
        var scope = new GrantScope(
            AreaId: ScopeAreaId,
            MoleculeId: ScopeMoleculeId,
            CompanyId: ScopeCompanyId,
            DepartmentId: ScopeDepartmentId,
            JobTypeId: ScopeJobTypeId
        );

        var assignment = await _roleService.AssignRoleAsync(
            SelectedUserId,
            SelectedRoleId,
            scope,
            currentUserId > 0 ? currentUserId : 1);

        if (assignment == null)
        {
            Error = _localizer["Error_RoleAssignmentFailed"];
            await LoadDropdownOptionsAsync();
            return Page();
        }

        _logger.LogInformation("Assigned role {Role} to user {UserId} by {AssignedBy}",
            roleTemplate.Key, SelectedUserId, currentUserId);

        await _auditLogService.LogAsync("RoleAssigned", "UserRoleAssignment", assignment.Id,
            $"Assigned role '{roleTemplate.Key}' to user '{user.DisplayName}' (UserId={SelectedUserId})");

        TempData["SuccessMessage"] = string.Format(_localizer["Success_RoleAssigned"],
            _localizer[roleTemplate.NameKey], user.DisplayName);

        return RedirectToPage("Index", new { ViewMode = "assignments", FilterUserId = SelectedUserId });
    }

    private string? ValidateScope(RoleScopeLevel scopeLevel)
    {
        return scopeLevel switch
        {
            RoleScopeLevel.Company when !ScopeCompanyId.HasValue =>
                _localizer["Error_CompanyScopeRequired"].Value,

            RoleScopeLevel.CompanyJobType when !ScopeCompanyId.HasValue || !ScopeJobTypeId.HasValue =>
                _localizer["Error_CompanyAndJobTypeScopeRequired"].Value,

            RoleScopeLevel.Department when !ScopeDepartmentId.HasValue =>
                _localizer["Error_DepartmentScopeRequired"].Value,

            RoleScopeLevel.Molecule when !ScopeMoleculeId.HasValue =>
                _localizer["Error_MoleculeScopeRequired"].Value,

            RoleScopeLevel.MoleculeJobType when !ScopeMoleculeId.HasValue || !ScopeJobTypeId.HasValue =>
                _localizer["Error_MoleculeAndJobTypeScopeRequired"].Value,

            RoleScopeLevel.Area when !ScopeAreaId.HasValue =>
                _localizer["Error_AreaScopeRequired"].Value,

            _ => null
        };
    }

    private async Task LoadDropdownOptionsAsync()
    {
        AvailableUsers = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.IsActive)
            .OrderBy(u => u.DisplayName)
            .Select(u => new UserOption(u.Id, u.DisplayName, u.Email))
            .ToListAsync();

        var activeRoles = await _roleService.GetRoleTemplatesAsync();
        AvailableRoles = activeRoles
            .Select(rt => new RoleTemplateOption(rt.Id, rt.Key, rt.NameKey, rt.ScopeLevel, rt.DescriptionKey))
            .ToList();

        AvailableProjects = await _db.Projects
            .IgnoreQueryFilters()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new ScopeOption(p.Id, p.DisplayName, "Project"))
            .ToListAsync();

        AvailableAreas = await _db.Areas
            .IgnoreQueryFilters()
            .Where(a => a.IsActive)
            .Include(a => a.Project)
            .OrderBy(a => a.Project.Name).ThenBy(a => a.Name)
            .Select(a => new ScopeOption(a.Id, $"{a.Project.DisplayName} / {a.DisplayName}", "Area"))
            .ToListAsync();

        AvailableMolecules = await _db.Molecules
            .IgnoreQueryFilters()
            .Where(m => m.IsActive)
            .Include(m => m.Area)
            .OrderBy(m => m.Area.Name).ThenBy(m => m.Name)
            .Select(m => new ScopeOption(m.Id, $"{m.Area.DisplayName} / {m.DisplayName}", "Molecule"))
            .ToListAsync();

        AvailableCompanies = await _db.Companies
            .IgnoreQueryFilters()
            .OrderBy(c => c.Name)
            .Select(c => new ScopeOption(c.Id, c.DisplayName ?? c.Name, "Company"))
            .ToListAsync();

        AvailableDepartments = await _db.Departments
            .IgnoreQueryFilters()
            .Where(d => d.IsActive)
            .Include(d => d.Molecule)
            .OrderBy(d => d.Molecule.Name).ThenBy(d => d.Name)
            .Select(d => new ScopeOption(d.Id, $"{d.Molecule.DisplayName} / {d.DisplayName}", "Department"))
            .ToListAsync();

        var activeJobTypes = await _jobTypeService.GetAllJobTypesAsync();
        AvailableJobTypes = activeJobTypes
            .Select(jt => new ScopeOption(jt.Id, $"{jt.Area?.DisplayName ?? ""} / {jt.DisplayName}", "JobType"))
            .ToList();
    }
}
