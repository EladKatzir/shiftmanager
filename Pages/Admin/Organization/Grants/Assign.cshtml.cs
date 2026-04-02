using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.Admin.Organization.Grants;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:AssignGrants policy;
// grant assignment needs cross-company hierarchy references for scope selection
[Authorize(Policy = "Grant:AssignGrants")]
public class AssignModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<AssignModel> _logger;
    private readonly IJobTypeService _jobTypeService;
    private readonly IAuditLogService _auditLogService;

    public AssignModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        ILogger<AssignModel> logger,
        IJobTypeService jobTypeService,
        IAuditLogService auditLogService) : base(localizer)
    {
        _db = db;
        _logger = logger;
        _jobTypeService = jobTypeService;
        _auditLogService = auditLogService;
    }

    public record UserOption(int Id, string DisplayName, string Email);
    public record GrantTypeOption(int Id, string Key, string NameKey, Models.Support.GrantCategory Category, string? DescriptionKey);
    public record ScopeOption(int Id, string Name, string Type);

    public List<UserOption> AvailableUsers { get; set; } = new();
    public List<GrantTypeOption> AvailableGrantTypes { get; set; } = new();
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
    public int SelectedGrantTypeId { get; set; }

    [BindProperty]
    public int? ScopeProjectId { get; set; }

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

    [BindProperty]
    public bool CanOwn { get; set; } = true;

    [BindProperty]
    public bool CanGive { get; set; }

    [BindProperty]
    public string? Notes { get; set; }

    public string? SelectedUserName { get; set; }

    public async Task OnGetAsync()
    {

        await LoadDropdownOptionsAsync();

        if (UserId.HasValue)
        {
            SelectedUserId = UserId.Value;
            var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == UserId.Value);
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

        if (SelectedGrantTypeId <= 0)
        {
            Error = _localizer["Error_GrantTypeRequired"];
            await LoadDropdownOptionsAsync();
            return Page();
        }

        // Verify user exists
        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == SelectedUserId);
        if (user == null)
        {
            Error = _localizer["Error_UserNotFound"];
            await LoadDropdownOptionsAsync();
            return Page();
        }

        // Verify grant type exists
        var grantType = await _db.GrantTypes.FindAsync(SelectedGrantTypeId);
        if (grantType == null)
        {
            Error = _localizer["Error_GrantTypeNotFound"];
            await LoadDropdownOptionsAsync();
            return Page();
        }

        // Check if grant already exists
        var existingGrant = await _db.Grants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(g =>
                g.UserId == SelectedUserId &&
                g.GrantTypeId == SelectedGrantTypeId &&
                g.ProjectId == ScopeProjectId &&
                g.AreaId == ScopeAreaId &&
                g.MoleculeId == ScopeMoleculeId &&
                g.CompanyId == ScopeCompanyId &&
                g.DepartmentId == ScopeDepartmentId &&
                g.JobTypeId == ScopeJobTypeId);

        if (existingGrant != null)
        {
            Error = _localizer["Error_GrantAlreadyExists"];
            await LoadDropdownOptionsAsync();
            return Page();
        }

        // Get current user ID for audit
        var currentUserIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        int.TryParse(currentUserIdClaim, out var currentUserId);

        var grant = new Grant
        {
            UserId = SelectedUserId,
            GrantTypeId = SelectedGrantTypeId,
            ProjectId = ScopeProjectId,
            AreaId = ScopeAreaId,
            MoleculeId = ScopeMoleculeId,
            CompanyId = ScopeCompanyId,
            DepartmentId = ScopeDepartmentId,
            JobTypeId = ScopeJobTypeId,
            CanOwn = CanOwn,
            CanGive = CanGive,
            GrantedByUserId = currentUserId > 0 ? currentUserId : null,
            GrantedAt = DateTime.UtcNow,
            Notes = Notes,
            IsAutoGrant = false
        };

        _db.Grants.Add(grant);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Assigned grant {GrantType} to user {UserId} by {GrantedBy}",
            grantType.Key, SelectedUserId, currentUserId);

        await _auditLogService.LogAsync("GrantAssigned", "Grant", grant.Id,
            $"Assigned grant '{grantType.Key}' to user '{user.DisplayName}' (UserId={SelectedUserId})");

        TempData["SuccessMessage"] = string.Format(_localizer["Success_GrantAssigned"],
            _localizer[grantType.NameKey], user.DisplayName);

        return RedirectToPage("Index", new { ViewMode = "grants", FilterUserId = SelectedUserId });
    }

    private async Task LoadDropdownOptionsAsync()
    {
        AvailableUsers = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.IsActive)
            .OrderBy(u => u.DisplayName)
            .Select(u => new UserOption(u.Id, u.DisplayName, u.Email))
            .ToListAsync();

        AvailableGrantTypes = await _db.GrantTypes
            .IgnoreQueryFilters()
            .Where(gt => gt.IsActive)
            .OrderBy(gt => gt.Category).ThenBy(gt => gt.Key)
            .Select(gt => new GrantTypeOption(gt.Id, gt.Key, gt.NameKey, gt.Category, gt.DescriptionKey))
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
