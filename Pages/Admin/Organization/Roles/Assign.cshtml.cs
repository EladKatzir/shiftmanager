using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using System.Security.Claims;

namespace ShiftManager.Pages.Admin.Organization.Roles;

[Authorize(Policy = "Grant:AssignRoles")]
public class AssignModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<AssignModel> _logger;

    public AssignModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        ILogger<AssignModel> logger) : base(localizer)
    {
        _db = db;
        _logger = logger;
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
        if (TempData["SuccessMessage"] is string successMsg) Success = successMsg;
        if (TempData["ErrorMessage"] is string errorMsg) Error = errorMsg;

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
        var roleTemplate = await _db.RoleTemplates.FindAsync(SelectedRoleId);
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

        // Check if assignment already exists (active)
        var existingAssignment = await _db.UserRoleAssignments
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ura =>
                ura.UserId == SelectedUserId &&
                ura.RoleTemplateId == SelectedRoleId &&
                ura.AreaId == ScopeAreaId &&
                ura.MoleculeId == ScopeMoleculeId &&
                ura.CompanyId == ScopeCompanyId &&
                ura.DepartmentId == ScopeDepartmentId &&
                ura.JobTypeId == ScopeJobTypeId &&
                ura.IsActive);

        if (existingAssignment != null)
        {
            Error = _localizer["Error_RoleAlreadyAssigned"];
            await LoadDropdownOptionsAsync();
            return Page();
        }

        // Get current user ID for audit
        var currentUserIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        int.TryParse(currentUserIdClaim, out var currentUserId);

        var assignment = new UserRoleAssignment
        {
            UserId = SelectedUserId,
            RoleTemplateId = SelectedRoleId,
            AreaId = ScopeAreaId,
            MoleculeId = ScopeMoleculeId,
            CompanyId = ScopeCompanyId,
            DepartmentId = ScopeDepartmentId,
            JobTypeId = ScopeJobTypeId,
            AssignedByUserId = currentUserId > 0 ? currentUserId : 1,
            AssignedAt = DateTime.UtcNow,
            IsActive = true
        };

        _db.UserRoleAssignments.Add(assignment);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Assigned role {Role} to user {UserId} by {AssignedBy}",
            roleTemplate.Key, SelectedUserId, currentUserId);

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

        AvailableRoles = await _db.RoleTemplates
            .IgnoreQueryFilters()
            .Where(rt => rt.IsActive)
            .OrderBy(rt => rt.SortOrder).ThenBy(rt => rt.Key)
            .Select(rt => new RoleTemplateOption(rt.Id, rt.Key, rt.NameKey, rt.ScopeLevel, rt.DescriptionKey))
            .ToListAsync();

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
            .Select(c => new ScopeOption(c.Id, c.Name, "Company"))
            .ToListAsync();

        AvailableDepartments = await _db.Departments
            .IgnoreQueryFilters()
            .Where(d => d.IsActive)
            .Include(d => d.Molecule)
            .OrderBy(d => d.Molecule.Name).ThenBy(d => d.Name)
            .Select(d => new ScopeOption(d.Id, $"{d.Molecule.DisplayName} / {d.DisplayName}", "Department"))
            .ToListAsync();

        AvailableJobTypes = await _db.JobTypes
            .IgnoreQueryFilters()
            .Where(jt => jt.IsActive)
            .Include(jt => jt.Area)
            .OrderBy(jt => jt.Area.Name).ThenBy(jt => jt.Name)
            .Select(jt => new ScopeOption(jt.Id, $"{jt.Area.DisplayName} / {jt.DisplayName}", "JobType"))
            .ToListAsync();
    }
}
