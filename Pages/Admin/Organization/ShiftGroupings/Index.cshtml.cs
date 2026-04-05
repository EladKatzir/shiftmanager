using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Helpers;
using ShiftManager.Models;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Pages.Admin.Organization.ShiftGroupings;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:ManageShiftGroupings policy;
// shift grouping management is inherently cross-company hierarchy data
[Authorize(Policy = "Grant:ManageShiftGroupings")]
public class IndexModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly IShiftGroupingService _shiftGroupingService;
    private readonly ILogger<IndexModel> _logger;
    private readonly IJobTypeService _jobTypeService;
    private readonly IAuditLogService _auditLogService;

    public IndexModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        IShiftGroupingService shiftGroupingService,
        ILogger<IndexModel> logger,
        IJobTypeService jobTypeService,
        IAuditLogService auditLogService) : base(localizer)
    {
        _db = db;
        _shiftGroupingService = shiftGroupingService;
        _logger = logger;
        _jobTypeService = jobTypeService;
        _auditLogService = auditLogService;
    }

    // View Models
    public record ShiftGroupingVM(int Id, string Name, string DisplayName, string MoleculeName, bool IsActive, int CompanyCount, int JobTypeCount, int UserCount, List<int> CompanyIds, List<int> JobTypeIds);
    public record MoleculeOption(int Id, string Name, string AreaName);
    public record CompanyOption(int Id, string Name);
    public record JobTypeOption(int Id, string Name, string AreaName);

    // Data
    public List<ShiftGroupingVM> ShiftGroupings { get; set; } = new();
    public List<MoleculeOption> AvailableMolecules { get; set; } = new();
    public List<CompanyOption> AvailableCompanies { get; set; } = new();
    public List<JobTypeOption> AvailableJobTypes { get; set; } = new();

    // Form Bindings
    [BindProperty] public string GroupingName { get; set; } = string.Empty;
    [BindProperty] public string GroupingDisplayName { get; set; } = string.Empty;
    [BindProperty] public int SelectedMoleculeId { get; set; }
    [BindProperty] public List<int> SelectedCompanyIds { get; set; } = new();
    [BindProperty] public List<int> SelectedJobTypeIds { get; set; } = new();

    [BindProperty] public int EditId { get; set; }
    [BindProperty] public string EditName { get; set; } = string.Empty;
    [BindProperty] public string EditDisplayName { get; set; } = string.Empty;
    [BindProperty] public List<int> EditCompanyIds { get; set; } = new();
    [BindProperty] public List<int> EditJobTypeIds { get; set; } = new();

    public async Task OnGetAsync()
    {

        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        ShiftGroupings = (await _db.ShiftGroupings
            .IgnoreQueryFilters()
            .Include(sg => sg.Molecule)
            .Include(sg => sg.Companies)
            .Include(sg => sg.JobTypes)
            .Select(sg => new ShiftGroupingVM(
                sg.Id,
                sg.Name,
                sg.DisplayName,
                sg.Molecule.DisplayName,
                sg.IsActive,
                sg.Companies.Count,
                sg.JobTypes.Count,
                0, // User count calculated separately if needed
                sg.Companies.Select(c => c.CompanyId).ToList(),
                sg.JobTypes.Select(jt => jt.JobTypeId).ToList()
            ))
            .ToListAsync())
            .OrderBy(sg => sg.MoleculeName)
            .ThenBy(sg => sg.Name)
            .ToList();

        AvailableMolecules = (await _db.Molecules
            .IgnoreQueryFilters()
            .Where(m => m.IsActive)
            .Include(m => m.Area)
            .Select(m => new MoleculeOption(m.Id, m.DisplayName, m.Area.DisplayName))
            .ToListAsync())
            .OrderBy(m => m.AreaName)
            .ThenBy(m => m.Name)
            .ToList();

        AvailableCompanies = await _db.Companies
            .IgnoreQueryFilters()
            .OrderBy(c => c.Name)
            .Select(c => new CompanyOption(c.Id, c.DisplayName ?? c.Name))
            .ToListAsync();

        var allActiveJobTypes = await _jobTypeService.GetAllJobTypesAsync();
        AvailableJobTypes = allActiveJobTypes
            .Select(jt => new JobTypeOption(jt.Id, jt.DisplayName, jt.Area?.DisplayName ?? ""))
            .OrderBy(jt => jt.AreaName)
            .ThenBy(jt => jt.Name)
            .ToList();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (string.IsNullOrWhiteSpace(GroupingName))
        {
            TempData["ErrorMessage"] = _localizer["Error_GroupingNameRequired"].Value;
            return RedirectToPage();
        }

        if (InputSanitizer.ContainsDangerousContent(GroupingName) || InputSanitizer.ContainsDangerousContent(GroupingDisplayName))
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidInput"].Value;
            return RedirectToPage();
        }

        if (SelectedMoleculeId <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_MoleculeRequired"].Value;
            return RedirectToPage();
        }

        var grouping = await _shiftGroupingService.CreateGroupingAsync(
            SelectedMoleculeId,
            GroupingName.Trim(),
            string.IsNullOrWhiteSpace(GroupingDisplayName) ? GroupingName.Trim() : GroupingDisplayName.Trim(),
            SelectedCompanyIds.Count > 0 ? SelectedCompanyIds : null,
            SelectedJobTypeIds.Count > 0 ? SelectedJobTypeIds : null
        );

        if (grouping == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_GroupingCreationFailed"].Value;
            return RedirectToPage();
        }

        _logger.LogInformation("Created ShiftGrouping {GroupingId}: {GroupingName} in Molecule {MoleculeId}",
            grouping.Id, grouping.Name, grouping.MoleculeId);

        await _auditLogService.LogAsync("GroupingCreated", "ShiftGrouping", grouping.Id,
            $"Created shift grouping '{grouping.DisplayName}' in molecule (MoleculeId={grouping.MoleculeId})");

        TempData["SuccessMessage"] = string.Format(_localizer["Success_GroupingCreated"], grouping.DisplayName);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEditAsync()
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

        var displayName = string.IsNullOrWhiteSpace(EditDisplayName) ? EditName.Trim() : EditDisplayName.Trim();

        var success = await _shiftGroupingService.UpdateGroupingAsync(
            EditId,
            name: EditName.Trim(),
            displayName: displayName,
            companyIds: EditCompanyIds,
            jobTypeIds: EditJobTypeIds);

        if (!success)
        {
            TempData["ErrorMessage"] = _localizer["Error_GroupingNotFound"].Value;
            return RedirectToPage();
        }

        _logger.LogInformation("ShiftGrouping {GroupingId} edited to '{NewName}'", EditId, EditName.Trim());

        await _auditLogService.LogAsync("GroupingEdited", "ShiftGrouping", EditId,
            $"Shift grouping edited to '{displayName}'");

        TempData["SuccessMessage"] = string.Format(_localizer["Success_GroupingEdited"], displayName);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(int id)
    {
        var grouping = await _db.ShiftGroupings.IgnoreQueryFilters().FirstOrDefaultAsync(sg => sg.Id == id);
        if (grouping == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_GroupingNotFound"].Value;
            return RedirectToPage();
        }

        if (grouping.IsActive)
        {
            await _shiftGroupingService.DeactivateGroupingAsync(id);
            TempData["SuccessMessage"] = string.Format(_localizer["Success_GroupingDeactivated"], grouping.DisplayName);
        }
        else
        {
            grouping.IsActive = true;
            await _db.SaveChangesAsync();
            TempData["SuccessMessage"] = string.Format(_localizer["Success_GroupingActivated"], grouping.DisplayName);
        }

        _logger.LogInformation("ShiftGrouping {GroupingId} ({GroupingName}) active status changed to {IsActive}",
            id, grouping.Name, grouping.IsActive);

        await _auditLogService.LogAsync("GroupingUpdated", "ShiftGrouping", id,
            $"Shift grouping '{grouping.DisplayName}' {(grouping.IsActive ? "activated" : "deactivated")}");

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var grouping = await _db.ShiftGroupings.IgnoreQueryFilters().FirstOrDefaultAsync(sg => sg.Id == id);
        if (grouping == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_GroupingNotFound"].Value;
            return RedirectToPage();
        }

        // Remove associations first
        var companies = await _db.ShiftGroupingCompanies.IgnoreQueryFilters().Where(sgc => sgc.ShiftGroupingId == id).ToListAsync();
        var jobTypes = await _db.ShiftGroupingJobTypes.IgnoreQueryFilters().Where(sgjt => sgjt.ShiftGroupingId == id).ToListAsync();

        _db.ShiftGroupingCompanies.RemoveRange(companies);
        _db.ShiftGroupingJobTypes.RemoveRange(jobTypes);
        _db.ShiftGroupings.Remove(grouping);

        await _db.SaveChangesAsync();

        _logger.LogInformation("Deleted ShiftGrouping {GroupingId}: {GroupingName}", id, grouping.Name);

        await _auditLogService.LogAsync("GroupingDeleted", "ShiftGrouping", id,
            $"Deleted shift grouping '{grouping.DisplayName}'");

        TempData["SuccessMessage"] = string.Format(_localizer["Success_GroupingDeleted"], grouping.DisplayName);
        return RedirectToPage();
    }
}
