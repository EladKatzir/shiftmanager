using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.Admin.Settings;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:ViewSettings policy;
// hierarchy dropdowns (Areas, Molecules, Companies) are reference data for settings navigation
[Authorize(Policy = "Grant:ViewSettings")]
public class IndexModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly IHierarchySettingsService _settingsService;
    private readonly ILogger<IndexModel> _logger;
    private readonly IAuditLogService _auditLogService;
    private readonly IGrantService _grantService;

    public IndexModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        IHierarchySettingsService settingsService,
        ILogger<IndexModel> logger,
        IAuditLogService auditLogService,
        IGrantService grantService) : base(localizer)
    {
        _db = db;
        _settingsService = settingsService;
        _logger = logger;
        _auditLogService = auditLogService;
        _grantService = grantService;
    }

    public record AreaOption(int Id, string Name);
    public record MoleculeOption(int Id, string Name, int AreaId);
    public record CompanyOption(int Id, string Name, int? MoleculeId);

    public List<AreaOption> AvailableAreas { get; set; } = new();
    public List<MoleculeOption> AvailableMolecules { get; set; } = new();
    public List<CompanyOption> AvailableCompanies { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string Level { get; set; } = "area"; // "area", "molecule", "company"

    [BindProperty(SupportsGet = true)]
    public int? SelectedId { get; set; }

    // Current settings
    public AreaSettingsDto? AreaSettings { get; set; }
    public MoleculeSettingsDto? MoleculeSettings { get; set; }
    public CompanySettingsDto? CompanySettings { get; set; }
    public EffectiveSettings? EffectiveSettings { get; set; }

    // Edit form
    [BindProperty]
    public int? EditRestHours { get; set; }

    [BindProperty]
    public int? EditWeeklyCap { get; set; }

    [BindProperty]
    public bool ClearRestHoursOverride { get; set; }

    [BindProperty]
    public bool ClearWeeklyCapOverride { get; set; }

    public async Task OnGetAsync()
    {
        await LoadDropdownsAsync();

        if (SelectedId.HasValue)
        {
            await LoadSettingsAsync();
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            TempData["ErrorMessage"] = _localizer["Error_NotAuthenticated"].Value;
            return RedirectToPage();
        }

        if (!SelectedId.HasValue)
        {
            TempData["ErrorMessage"] = _localizer["Error_NoEntitySelected"].Value;
            return RedirectToPage(new { Level });
        }

        // F10a SECURITY: the class gate (ViewSettings) is view-only. Mutating work-hour/rest-hour
        // limits requires the dedicated edit grant, scoped to the selected entity. These edit grants
        // are co-distributed with ViewSettings, so legitimate editors retain access.
        bool canEdit = Level switch
        {
            "company" => await _grantService.HasGrantWithScopeAsync(userId, "EditCompanySettings", companyId: SelectedId.Value),
            "molecule" => await _grantService.HasGrantWithScopeAsync(userId, "EditMoleculeSettings", moleculeId: SelectedId.Value),
            "area" => await _grantService.HasGrantWithScopeAsync(userId, "EditMoleculeSettings", areaId: SelectedId.Value),
            _ => false
        };
        if (!canEdit)
        {
            _logger.LogWarning("Unauthorized settings edit attempt by user {UserId} at level {Level} id {SelectedId}", userId, Level, SelectedId);
            return Forbid();
        }

        bool result = false;

        switch (Level)
        {
            case "area":
                if (!EditRestHours.HasValue || !EditWeeklyCap.HasValue)
                {
                    TempData["ErrorMessage"] = _localizer["Error_AreaSettingsRequired"].Value;
                    return RedirectToPage(new { Level, SelectedId });
                }
                result = await _settingsService.UpdateAreaSettingsAsync(
                    SelectedId.Value,
                    EditRestHours.Value,
                    EditWeeklyCap.Value,
                    userId);
                break;

            case "molecule":
                var restHoursOverride = ClearRestHoursOverride ? null : EditRestHours;
                var weeklyCapOverride = ClearWeeklyCapOverride ? null : EditWeeklyCap;
                result = await _settingsService.UpdateMoleculeSettingsAsync(
                    SelectedId.Value,
                    restHoursOverride,
                    weeklyCapOverride,
                    userId);
                break;

            case "company":
                var companyRestHoursOverride = ClearRestHoursOverride ? null : EditRestHours;
                var companyWeeklyCapOverride = ClearWeeklyCapOverride ? null : EditWeeklyCap;
                result = await _settingsService.UpdateCompanySettingsAsync(
                    SelectedId.Value,
                    companyRestHoursOverride,
                    companyWeeklyCapOverride,
                    userId);
                break;
        }

        if (result)
        {
            await _auditLogService.LogAsync(
                "SettingsUpdated",
                Level == "area" ? "AreaSettings" : Level == "molecule" ? "MoleculeSettings" : "CompanySettings",
                SelectedId,
                $"{Level} settings updated: RestHours={EditRestHours}, WeeklyCap={EditWeeklyCap}");
            TempData["SuccessMessage"] = _localizer["Success_SettingsSaved"].Value;
        }
        else
        {
            TempData["ErrorMessage"] = _localizer["Error_FailedToSaveSettings"].Value;
        }

        return RedirectToPage(new { Level, SelectedId });
    }

    private async Task LoadDropdownsAsync()
    {
        AvailableAreas = await _db.Areas.IgnoreQueryFilters()
            .Where(a => a.IsActive)
            .Include(a => a.Project)
            .OrderBy(a => a.Project.Name).ThenBy(a => a.Name)
            .Select(a => new AreaOption(a.Id, $"{a.Project.DisplayName} / {a.DisplayName}"))
            .ToListAsync();

        AvailableMolecules = await _db.Molecules.IgnoreQueryFilters()
            .Where(m => m.IsActive)
            .Include(m => m.Area)
            .OrderBy(m => m.Area.Name).ThenBy(m => m.Name)
            .Select(m => new MoleculeOption(m.Id, $"{m.Area.DisplayName} / {m.DisplayName}", m.AreaId))
            .ToListAsync();

        var companiesRaw = await _db.Companies.IgnoreQueryFilters()
            .Include(c => c.Molecule)
            .Select(c => new { c.Id, c.Name, c.DisplayName, c.NameHe, c.MoleculeId, MoleculeDisplayName = c.Molecule != null ? c.Molecule.DisplayName : null })
            .ToListAsync();
        var cultureComparer = StringComparer.Create(System.Globalization.CultureInfo.CurrentUICulture, ignoreCase: true);
        AvailableCompanies = companiesRaw.Select(c =>
        {
            var resolved = Company.ResolveLocalizedName(c.Name, c.DisplayName, c.NameHe);
            var label = !string.IsNullOrEmpty(c.MoleculeDisplayName) ? $"{c.MoleculeDisplayName} / {resolved}" : resolved;
            return new CompanyOption(c.Id, label, c.MoleculeId);
        })
        .OrderBy(c => c.Name, cultureComparer)
        .ToList();
    }

    private async Task LoadSettingsAsync()
    {
        switch (Level)
        {
            case "area":
                AreaSettings = await _settingsService.GetAreaSettingsAsync(SelectedId!.Value);
                if (AreaSettings != null)
                {
                    EditRestHours = AreaSettings.DefaultRestHours;
                    EditWeeklyCap = AreaSettings.DefaultWeeklyCap;
                }
                break;

            case "molecule":
                MoleculeSettings = await _settingsService.GetMoleculeSettingsOverrideAsync(SelectedId!.Value);
                EffectiveSettings = await _settingsService.GetMoleculeSettingsAsync(SelectedId!.Value);
                if (MoleculeSettings != null)
                {
                    EditRestHours = MoleculeSettings.RestHoursOverride;
                    EditWeeklyCap = MoleculeSettings.WeeklyCapOverride;
                }
                break;

            case "company":
                CompanySettings = await _settingsService.GetCompanySettingsOverrideAsync(SelectedId!.Value);
                EffectiveSettings = await _settingsService.GetEffectiveSettingsAsync(SelectedId!.Value);
                if (CompanySettings != null)
                {
                    EditRestHours = CompanySettings.RestHoursOverride;
                    EditWeeklyCap = CompanySettings.WeeklyCapOverride;
                }
                break;
        }
    }
}
