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

namespace ShiftManager.Pages.Owner.Hub.RoleTemplates;

/// <summary>
/// Create a new custom role template with optional job-type labels.
/// </summary>
[Authorize(Policy = "Grant:AdminAccess")]
public class CreateModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<CreateModel> _logger;
    private readonly IJobTypeService _jobTypeService;
    private readonly IAuditLogService _auditLogService;

    public CreateModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        ILogger<CreateModel> logger,
        IJobTypeService jobTypeService,
        IAuditLogService auditLogService) : base(localizer)
    {
        _db = db;
        _logger = logger;
        _jobTypeService = jobTypeService;
        _auditLogService = auditLogService;
    }

    [BindProperty] public string Key { get; set; } = string.Empty;
    [BindProperty] public string? DisplayNameEN { get; set; }
    [BindProperty] public string? DisplayNameHE { get; set; }
    [BindProperty] public UserRole DerivedUserRole { get; set; } = UserRole.Employee;
    [BindProperty] public RoleScopeLevel ScopeLevel { get; set; } = RoleScopeLevel.Company;
    [BindProperty] public bool CanBeAssignedByDefault { get; set; } = true;
    [BindProperty] public bool IsVisibleInSignup { get; set; } = true;
    [BindProperty] public int SortOrder { get; set; } = 100;

    // Job-type labels (submitted as parallel arrays)
    [BindProperty] public List<int> LabelJobTypeIds { get; set; } = new();
    [BindProperty] public List<string> LabelDisplayNamesEN { get; set; } = new();
    [BindProperty] public List<string> LabelDisplayNamesHE { get; set; } = new();

    public List<JobType> AvailableJobTypes { get; set; } = new();
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        await LoadJobTypesAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadJobTypesAsync();

        if (string.IsNullOrWhiteSpace(Key))
        {
            ErrorMessage = "Key is required.";
            return Page();
        }

        // Sanitize key — alphanumeric + underscore only
        Key = Key.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(Key, @"^[A-Za-z_][A-Za-z0-9_]*$"))
        {
            ErrorMessage = _localizer["Error_RoleTemplate_InvalidKey"].Value;
            return Page();
        }

        // Check uniqueness
        var exists = await _db.RoleTemplates.AnyAsync(rt => rt.Key == Key);
        if (exists)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, _localizer["Error_RoleTemplate_KeyExists"].Value, Key);
            return Page();
        }

        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();

            var template = new RoleTemplate
            {
                Key = Key,
                NameKey = $"Role_{Key}",
                DescriptionKey = $"RoleDesc_{Key}",
                DerivedUserRole = DerivedUserRole,
                ScopeLevel = ScopeLevel,
                DisplayNameEN = DisplayNameEN?.Trim(),
                DisplayNameHE = DisplayNameHE?.Trim(),
                IsSystem = false,
                IsActive = true,
                CanBeAssignedByDefault = CanBeAssignedByDefault,
                IsVisibleInSignup = IsVisibleInSignup,
                SortOrder = SortOrder,
                CreatedAt = DateTime.UtcNow
            };

            _db.RoleTemplates.Add(template);
            await _db.SaveChangesAsync();

            // Add job-type labels
            for (int i = 0; i < LabelJobTypeIds.Count; i++)
            {
                var jobTypeId = LabelJobTypeIds[i];
                var nameEN = i < LabelDisplayNamesEN.Count ? LabelDisplayNamesEN[i]?.Trim() : null;
                var nameHE = i < LabelDisplayNamesHE.Count ? LabelDisplayNamesHE[i]?.Trim() : null;

                if (jobTypeId > 0 && (!string.IsNullOrEmpty(nameEN) || !string.IsNullOrEmpty(nameHE)))
                {
                    _db.Set<RoleTemplateJobTypeLabel>().Add(new RoleTemplateJobTypeLabel
                    {
                        RoleTemplateId = template.Id,
                        JobTypeId = jobTypeId,
                        DisplayNameEN = nameEN ?? string.Empty,
                        DisplayNameHE = nameHE ?? string.Empty
                    });
                }
            }

            await _db.SaveChangesAsync();

            await transaction.CommitAsync();

            _logger.LogInformation("Custom role template created: {Key} (Id={Id})", template.Key, template.Id);

            await _auditLogService.LogAsync("RoleTemplateCreated", "RoleTemplate", template.Id,
                $"Created role template '{template.Key}' with scope {template.ScopeLevel} and role {template.DerivedUserRole}");

            return RedirectToPage("Edit", new { id = template.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating role template");
            ErrorMessage = _localizer["Error_UnexpectedError"].Value;
            return Page();
        }
    }

    private async Task LoadJobTypesAsync()
    {
        AvailableJobTypes = await _jobTypeService.GetAllJobTypesAsync();
    }
}
