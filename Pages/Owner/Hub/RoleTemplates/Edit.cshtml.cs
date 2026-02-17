using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Pages.Owner.Hub.RoleTemplates;

/// <summary>
/// Edit a role template: metadata, job-type labels, and grant assignment.
/// System templates allow grant editing but not key/scope changes.
/// </summary>
// SECURITY-AUDITED: IgnoreQueryFilters() used for cross-tenant grant operations — Owner page requires Grant:AdminAccess
[Authorize(Policy = "Grant:AdminAccess")]
public class EditModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IGrantService _grantService;
    private readonly ILogger<EditModel> _logger;

    public EditModel(AppDbContext db, IGrantService grantService, ILogger<EditModel> logger)
    {
        _db = db;
        _grantService = grantService;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)] public int Id { get; set; }
    [BindProperty] public string? DisplayNameEN { get; set; }
    [BindProperty] public string? DisplayNameHE { get; set; }
    [BindProperty] public UserRole DerivedUserRole { get; set; }
    [BindProperty] public RoleScopeLevel ScopeLevel { get; set; }
    [BindProperty] public bool CanBeAssignedByDefault { get; set; }
    [BindProperty] public bool IsVisibleInSignup { get; set; }
    [BindProperty] public bool IsActive { get; set; }
    [BindProperty] public int SortOrder { get; set; }

    // Job-type labels (parallel arrays)
    [BindProperty] public List<int> LabelJobTypeIds { get; set; } = new();
    [BindProperty] public List<string> LabelDisplayNamesEN { get; set; } = new();
    [BindProperty] public List<string> LabelDisplayNamesHE { get; set; } = new();

    public RoleTemplate? Template { get; set; }
    public List<RoleTemplateGrant> TemplateGrants { get; set; } = new();
    public List<GrantType> AllGrantTypes { get; set; } = new();
    public List<JobType> AvailableJobTypes { get; set; } = new();
    public List<RoleTemplateJobTypeLabel> ExistingLabels { get; set; } = new();
    public int UserCount { get; set; }

    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var loaded = await LoadTemplateAsync();
        if (!loaded) return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnPostMetadataAsync()
    {
        var template = await _db.RoleTemplates.FindAsync(Id);
        if (template == null) return NotFound();

        try
        {
            // Custom templates can change more fields; system templates lock Key, ScopeLevel, DerivedUserRole
            if (!template.IsSystem)
            {
                template.ScopeLevel = ScopeLevel;
                template.DerivedUserRole = DerivedUserRole;
            }

            template.DisplayNameEN = DisplayNameEN?.Trim();
            template.DisplayNameHE = DisplayNameHE?.Trim();
            template.CanBeAssignedByDefault = CanBeAssignedByDefault;
            template.IsVisibleInSignup = IsVisibleInSignup;
            template.IsActive = IsActive;
            template.SortOrder = SortOrder;

            await _db.SaveChangesAsync();

            _logger.LogInformation("Role template metadata updated: {Key} (Id={Id})", template.Key, template.Id);
            SuccessMessage = "Template metadata updated successfully.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating role template metadata {Id}", Id);
            ErrorMessage = "An unexpected error occurred. Please try again.";
        }

        await LoadTemplateAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostLabelsAsync()
    {
        var template = await _db.RoleTemplates.FindAsync(Id);
        if (template == null) return NotFound();

        try
        {
            // Replace job-type labels (delete-then-add)
            var existingLabels = await _db.Set<RoleTemplateJobTypeLabel>()
                .Where(l => l.RoleTemplateId == Id)
                .ToListAsync();
            _db.Set<RoleTemplateJobTypeLabel>().RemoveRange(existingLabels);

            for (int i = 0; i < LabelJobTypeIds.Count; i++)
            {
                var jobTypeId = LabelJobTypeIds[i];
                var nameEN = i < LabelDisplayNamesEN.Count ? LabelDisplayNamesEN[i]?.Trim() : null;
                var nameHE = i < LabelDisplayNamesHE.Count ? LabelDisplayNamesHE[i]?.Trim() : null;

                if (jobTypeId > 0 && (!string.IsNullOrEmpty(nameEN) || !string.IsNullOrEmpty(nameHE)))
                {
                    _db.Set<RoleTemplateJobTypeLabel>().Add(new RoleTemplateJobTypeLabel
                    {
                        RoleTemplateId = Id,
                        JobTypeId = jobTypeId,
                        DisplayNameEN = nameEN ?? string.Empty,
                        DisplayNameHE = nameHE ?? string.Empty
                    });
                }
            }

            await _db.SaveChangesAsync();

            _logger.LogInformation("Role template labels updated: {Key} (Id={Id})", template.Key, template.Id);
            SuccessMessage = "Job type labels updated successfully.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating role template labels {Id}", Id);
            ErrorMessage = "An unexpected error occurred. Please try again.";
        }

        await LoadTemplateAsync();
        return Page();
    }

    /// <summary>
    /// Add a grant to this role template (AJAX).
    /// </summary>
    public async Task<IActionResult> OnPostAddGrantAsync([FromBody] AddGrantRequest request)
    {
        try
        {
            var exists = await _db.RoleTemplateGrants
                .AnyAsync(rtg => rtg.RoleTemplateId == Id && rtg.GrantTypeId == request.GrantTypeId);

            if (exists)
                return new JsonResult(new { success = false, error = "Grant already assigned to this template" });

            var grant = new RoleTemplateGrant
            {
                RoleTemplateId = Id,
                GrantTypeId = request.GrantTypeId,
                CanOwn = request.CanOwn,
                CanGive = request.CanGive,
                ScopeMode = request.ScopeMode,
                IsOverride = true
            };

            _db.RoleTemplateGrants.Add(grant);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Grant added to template {TemplateId}: GrantTypeId={GrantTypeId}", Id, request.GrantTypeId);
            return new JsonResult(new { success = true, grantId = grant.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding grant to template {TemplateId}", Id);
            return new JsonResult(new { success = false, error = "An error occurred" });
        }
    }

    /// <summary>
    /// Remove a grant from this role template (AJAX).
    /// </summary>
    public async Task<IActionResult> OnPostRemoveGrantAsync([FromBody] RemoveGrantRequest request)
    {
        try
        {
            var grant = await _db.RoleTemplateGrants.FindAsync(request.GrantId);
            if (grant == null || grant.RoleTemplateId != Id)
                return new JsonResult(new { success = false, error = "Grant not found" });

            _db.RoleTemplateGrants.Remove(grant);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Grant removed from template {TemplateId}: GrantId={GrantId}", Id, request.GrantId);
            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing grant from template {TemplateId}", Id);
            return new JsonResult(new { success = false, error = "An error occurred" });
        }
    }

    private async Task<bool> LoadTemplateAsync()
    {
        Template = await _db.RoleTemplates
            .Include(rt => rt.AutoGrants)
                .ThenInclude(ag => ag.GrantType)
            .Include(rt => rt.JobTypeLabels)
                .ThenInclude(l => l.JobType)
            .FirstOrDefaultAsync(rt => rt.Id == Id);

        if (Template == null) return false;

        // Populate bind properties from loaded template
        DisplayNameEN = Template.DisplayNameEN;
        DisplayNameHE = Template.DisplayNameHE;
        DerivedUserRole = Template.DerivedUserRole ?? UserRole.Employee;
        ScopeLevel = Template.ScopeLevel;
        CanBeAssignedByDefault = Template.CanBeAssignedByDefault;
        IsVisibleInSignup = Template.IsVisibleInSignup;
        IsActive = Template.IsActive;
        SortOrder = Template.SortOrder;

        TemplateGrants = Template.AutoGrants.OrderBy(ag => ag.GrantType.Category).ThenBy(ag => ag.GrantType.Key).ToList();
        ExistingLabels = Template.JobTypeLabels.ToList();

        AllGrantTypes = await _db.GrantTypes
            .Where(gt => gt.IsActive)
            .OrderBy(gt => gt.Category)
            .ThenBy(gt => gt.Key)
            .ToListAsync();

        AvailableJobTypes = await _db.JobTypes
            .Where(jt => jt.IsActive)
            .OrderBy(jt => jt.SortOrder)
            .ThenBy(jt => jt.DisplayName ?? jt.Name)
            .ToListAsync();

        // SECURITY-AUDITED: Cross-tenant user count — Owner page requires Grant:AdminAccess
        UserCount = await _db.Users
            .IgnoreQueryFilters()
            .CountAsync(u => u.IsActive && u.RoleTemplateId == Id);

        return true;
    }

    public class AddGrantRequest
    {
        public int GrantTypeId { get; set; }
        public bool CanOwn { get; set; } = true;
        public bool CanGive { get; set; }
        public GrantScopeMode ScopeMode { get; set; } = GrantScopeMode.SameAsRole;
    }

    public class RemoveGrantRequest
    {
        public int GrantId { get; set; }
    }
}
