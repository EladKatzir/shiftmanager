using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
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

    public IndexModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        IShiftGroupingService shiftGroupingService,
        ILogger<IndexModel> logger) : base(localizer)
    {
        _db = db;
        _shiftGroupingService = shiftGroupingService;
        _logger = logger;
    }

    // View Models
    public record ShiftGroupingVM(int Id, string Name, string DisplayName, string MoleculeName, bool IsActive, int CompanyCount, int JobTypeCount, int UserCount);
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

    public async Task OnGetAsync()
    {
        if (TempData["SuccessMessage"] is string successMsg)
            Success = successMsg;
        if (TempData["ErrorMessage"] is string errorMsg)
            Error = errorMsg;

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
                0 // User count calculated separately if needed
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

        AvailableJobTypes = (await _db.JobTypes
            .IgnoreQueryFilters()
            .Where(jt => jt.IsActive)
            .Include(jt => jt.Area)
            .Select(jt => new JobTypeOption(jt.Id, jt.DisplayName, jt.Area.DisplayName))
            .ToListAsync())
            .OrderBy(jt => jt.AreaName)
            .ThenBy(jt => jt.Name)
            .ToList();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (string.IsNullOrWhiteSpace(GroupingName))
        {
            TempData["ErrorMessage"] = _localizer["Error_GroupingNameRequired"];
            return RedirectToPage();
        }

        if (SelectedMoleculeId <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_MoleculeRequired"];
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
            TempData["ErrorMessage"] = _localizer["Error_GroupingCreationFailed"];
            return RedirectToPage();
        }

        _logger.LogInformation("Created ShiftGrouping {GroupingId}: {GroupingName} in Molecule {MoleculeId}",
            grouping.Id, grouping.Name, grouping.MoleculeId);

        TempData["SuccessMessage"] = string.Format(_localizer["Success_GroupingCreated"], grouping.DisplayName);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(int id)
    {
        var grouping = await _db.ShiftGroupings.FindAsync(id);
        if (grouping == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_GroupingNotFound"];
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

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var grouping = await _db.ShiftGroupings.FindAsync(id);
        if (grouping == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_GroupingNotFound"];
            return RedirectToPage();
        }

        // Remove associations first
        var companies = await _db.ShiftGroupingCompanies.Where(sgc => sgc.ShiftGroupingId == id).ToListAsync();
        var jobTypes = await _db.ShiftGroupingJobTypes.Where(sgjt => sgjt.ShiftGroupingId == id).ToListAsync();

        _db.ShiftGroupingCompanies.RemoveRange(companies);
        _db.ShiftGroupingJobTypes.RemoveRange(jobTypes);
        _db.ShiftGroupings.Remove(grouping);

        await _db.SaveChangesAsync();

        _logger.LogInformation("Deleted ShiftGrouping {GroupingId}: {GroupingName}", id, grouping.Name);

        TempData["SuccessMessage"] = string.Format(_localizer["Success_GroupingDeleted"], grouping.DisplayName);
        return RedirectToPage();
    }
}
