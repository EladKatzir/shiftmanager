using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Pages.Admin.Organization.ChoreTypes;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:EditChoreTypes policy;
// chore type management is molecule-scoped configuration data
[Authorize(Policy = "Grant:EditChoreTypes")]
public class IndexModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<IndexModel> _logger;
    private readonly IChoreTypeService _choreTypeService;
    private readonly IGrantService _grantService;
    private readonly ICompanyContext _companyContext;
    private readonly IAuditLogService _auditLogService;

    public IndexModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        ILogger<IndexModel> logger,
        IChoreTypeService choreTypeService,
        IGrantService grantService,
        ICompanyContext companyContext,
        IAuditLogService auditLogService) : base(localizer)
    {
        _db = db;
        _logger = logger;
        _choreTypeService = choreTypeService;
        _grantService = grantService;
        _companyContext = companyContext;
        _auditLogService = auditLogService;
    }

    // View Models
    public record ChoreTypeVM(int Id, string Name, string DisplayName, string? Color, int SortOrder, string MoleculeName, bool IsActive, int ChoreCount, string? NameEn, string? NameHe);
    public record MoleculeOption(int Id, string Name);

    // Data
    public List<ChoreTypeVM> ChoreTypes { get; set; } = new();
    public List<MoleculeOption> AvailableMolecules { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int? MoleculeId { get; set; }

    // Create form
    [BindProperty] public string ChoreTypeName { get; set; } = string.Empty;
    [BindProperty] public string ChoreTypeDisplayName { get; set; } = string.Empty;
    [BindProperty] public string? ChoreTypeNameEn { get; set; }
    [BindProperty] public string? ChoreTypeNameHe { get; set; }
    [BindProperty] public string? ChoreTypeColor { get; set; }

    // Edit form
    [BindProperty] public int EditId { get; set; }
    [BindProperty] public string EditDisplayName { get; set; } = string.Empty;
    [BindProperty] public string? EditNameEn { get; set; }
    [BindProperty] public string? EditNameHe { get; set; }
    [BindProperty] public string? EditColor { get; set; }
    [BindProperty] public int EditSortOrder { get; set; }

    public async Task OnGetAsync()
    {

        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId)) return;

        var companyId = _companyContext.CompanyId;
        if (!companyId.HasValue) return;

        var userCompany = await _db.Companies
            .Include(c => c.Molecule)
            .FirstOrDefaultAsync(c => c.Id == companyId.Value);
        var userMoleculeId = userCompany?.MoleculeId;

        // Load accessible molecules via grants + user's own molecule as fallback
        var moleculeIds = new HashSet<int>(
            await _grantService.GetAccessibleMoleculeIdsForGrantAsync(currentUserId, "EditChoreTypes"));

        if (userMoleculeId.HasValue)
            moleculeIds.Add(userMoleculeId.Value);

        // SECURITY-AUDITED: SAFE — filtered to only molecules the user has grant access to
        AvailableMolecules = await _db.Molecules
            .IgnoreQueryFilters()
            .Where(m => moleculeIds.Contains(m.Id) && m.IsActive)
            .OrderBy(m => m.DisplayName)
            .Select(m => new MoleculeOption(m.Id, m.DisplayName))
            .ToListAsync();

        // Default MoleculeId to user's own molecule if not set
        if (!MoleculeId.HasValue && userMoleculeId.HasValue)
            MoleculeId = userMoleculeId.Value;

        // Validate selected molecule is accessible
        if (MoleculeId.HasValue && !AvailableMolecules.Any(m => m.Id == MoleculeId.Value))
            MoleculeId = userMoleculeId;

        if (MoleculeId.HasValue)
        {
            // SECURITY-AUDITED: SAFE — scoped to specific molecule; includes inactive for admin management
            var choreTypes = await _db.ChoreTypes
                .IgnoreQueryFilters()
                .Where(ct => ct.MoleculeId == MoleculeId.Value)
                .Include(ct => ct.Molecule)
                .OrderBy(ct => ct.SortOrder)
                .ThenBy(ct => ct.DisplayName)
                .ToListAsync();

            // Count active chores per type for delete guard
            var choreCountsByType = await _db.Chores
                .Where(c => c.ChoreTypeId.HasValue && c.CanceledAt == null)
                .GroupBy(c => c.ChoreTypeId!.Value)
                .Select(g => new { ChoreTypeId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ChoreTypeId, x => x.Count);

            ChoreTypes = choreTypes
                .Select(ct => new ChoreTypeVM(
                    ct.Id,
                    ct.Name,
                    ct.DisplayName,
                    ct.Color,
                    ct.SortOrder,
                    ct.Molecule.DisplayName,
                    ct.IsActive,
                    choreCountsByType.GetValueOrDefault(ct.Id, 0),
                    ct.NameEn,
                    ct.NameHe
                ))
                .ToList();
        }
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (string.IsNullOrWhiteSpace(ChoreTypeName))
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreTypeNameRequired"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        if (string.IsNullOrWhiteSpace(ChoreTypeDisplayName))
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreTypeDisplayNameRequired"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        if (!MoleculeId.HasValue || MoleculeId.Value <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_MoleculeRequired"].Value;
            return RedirectToPage();
        }

        if (ChoreTypeName.Trim().Length > 100)
        {
            TempData["ErrorMessage"] = _localizer["Error_NameTooLong"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        if (!string.IsNullOrWhiteSpace(ChoreTypeNameEn) && ChoreTypeNameEn.Trim().Length > 100)
        {
            TempData["ErrorMessage"] = _localizer["Error_NameTooLong"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        if (!string.IsNullOrWhiteSpace(ChoreTypeNameHe) && ChoreTypeNameHe.Trim().Length > 100)
        {
            TempData["ErrorMessage"] = _localizer["Error_NameTooLong"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
        {
            TempData["ErrorMessage"] = _localizer["Error_UserNotFound"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        // Verify user has grant access to this molecule (prevent IDOR)
        var accessibleMoleculeIds = new HashSet<int>(
            await _grantService.GetAccessibleMoleculeIdsForGrantAsync(currentUserId, "EditChoreTypes"));
        var userCompanyForAuth = await _db.Companies.Include(c => c.Molecule)
            .FirstOrDefaultAsync(c => c.Id == _companyContext.CompanyId);
        if (userCompanyForAuth?.MoleculeId.HasValue == true)
            accessibleMoleculeIds.Add(userCompanyForAuth.MoleculeId.Value);

        if (!accessibleMoleculeIds.Contains(MoleculeId.Value))
        {
            TempData["ErrorMessage"] = _localizer["Error_MoleculeNotFound"].Value;
            return RedirectToPage();
        }

        // Sanitize color to prevent CSS injection
        var safeColor = SanitizeColor(ChoreTypeColor);

        var choreType = await _choreTypeService.CreateAsync(
            MoleculeId.Value,
            ChoreTypeName.Trim(),
            ChoreTypeDisplayName.Trim(),
            safeColor,
            currentUserId,
            string.IsNullOrWhiteSpace(ChoreTypeNameEn) ? null : ChoreTypeNameEn.Trim(),
            string.IsNullOrWhiteSpace(ChoreTypeNameHe) ? null : ChoreTypeNameHe.Trim());

        _logger.LogInformation("Created ChoreType {ChoreTypeId}: {ChoreTypeName} in Molecule {MoleculeId}",
            choreType.Id, choreType.Name, MoleculeId.Value);

        await _auditLogService.LogAsync("ChoreTypeCreated", "ChoreType", choreType.Id,
            $"Created chore type '{choreType.DisplayName}' in molecule (MoleculeId={MoleculeId.Value})");

        TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Success_ChoreTypeCreated"], choreType.DisplayName);
        return RedirectToPage(new { MoleculeId });
    }

    public async Task<IActionResult> OnPostUpdateAsync()
    {
        if (EditId <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreTypeNotFound"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        if (string.IsNullOrWhiteSpace(EditDisplayName))
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreTypeDisplayNameRequired"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        if (!string.IsNullOrWhiteSpace(EditNameEn) && EditNameEn.Trim().Length > 100)
        {
            TempData["ErrorMessage"] = _localizer["Error_NameTooLong"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        if (!string.IsNullOrWhiteSpace(EditNameHe) && EditNameHe.Trim().Length > 100)
        {
            TempData["ErrorMessage"] = _localizer["Error_NameTooLong"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        // Verify user has access to this chore type's molecule (prevent IDOR)
        var existing = await _choreTypeService.GetByIdAsync(EditId);
        if (existing == null || !await IsUserAuthorizedForMoleculeAsync(existing.MoleculeId))
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreTypeNotFound"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        try
        {
            var choreType = await _choreTypeService.UpdateAsync(
                EditId,
                EditDisplayName.Trim(),
                SanitizeColor(EditColor),
                EditSortOrder,
                string.IsNullOrWhiteSpace(EditNameEn) ? null : EditNameEn.Trim(),
                string.IsNullOrWhiteSpace(EditNameHe) ? null : EditNameHe.Trim());

            _logger.LogInformation("Updated ChoreType {ChoreTypeId}: {ChoreTypeName}",
                choreType.Id, choreType.DisplayName);

            await _auditLogService.LogAsync("ChoreTypeUpdated", "ChoreType", choreType.Id,
                $"Updated chore type '{choreType.DisplayName}'");

            TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Success_ChoreTypeUpdated"], choreType.DisplayName);
        }
        catch (ArgumentException)
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreTypeNotFound"].Value;
        }

        return RedirectToPage(new { MoleculeId });
    }

    public async Task<IActionResult> OnPostDeactivateAsync(int id)
    {
        var choreType = await _choreTypeService.GetByIdAsync(id);
        if (choreType == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreTypeNotFound"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        // Verify user has access to this chore type's molecule
        if (!await IsUserAuthorizedForMoleculeAsync(choreType.MoleculeId))
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreTypeNotFound"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        var success = await _choreTypeService.DeactivateAsync(id);
        if (!success)
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreTypeNotFound"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        _logger.LogInformation("Deactivated ChoreType {ChoreTypeId}: {ChoreTypeName}",
            id, choreType.DisplayName);

        await _auditLogService.LogAsync("ChoreTypeDeleted", "ChoreType", id,
            $"Deactivated chore type '{choreType.DisplayName}'");

        TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Success_ChoreTypeDeactivated"], choreType.DisplayName);
        return RedirectToPage(new { MoleculeId });
    }

    public async Task<IActionResult> OnPostActivateAsync(int id)
    {
        // SECURITY-AUDITED: SAFE — scoped by specific ChoreType id; molecule access verified below
        var choreType = await _db.ChoreTypes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ct => ct.Id == id);

        if (choreType == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreTypeNotFound"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        // Verify user has access to this chore type's molecule
        if (!await IsUserAuthorizedForMoleculeAsync(choreType.MoleculeId))
        {
            TempData["ErrorMessage"] = _localizer["Error_ChoreTypeNotFound"].Value;
            return RedirectToPage(new { MoleculeId });
        }

        choreType.IsActive = true;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Activated ChoreType {ChoreTypeId}: {ChoreTypeName}",
            id, choreType.DisplayName);

        await _auditLogService.LogAsync("ChoreTypeUpdated", "ChoreType", id,
            $"Activated chore type '{choreType.DisplayName}'");

        TempData["SuccessMessage"] = string.Format(CultureInfo.CurrentCulture, _localizer["Success_ChoreTypeActivated"], choreType.DisplayName);
        return RedirectToPage(new { MoleculeId });
    }

    /// <summary>
    /// Verifies the current user has grant access to the specified molecule.
    /// </summary>
    private async Task<bool> IsUserAuthorizedForMoleculeAsync(int moleculeId)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId)) return false;

        var accessibleMoleculeIds = new HashSet<int>(
            await _grantService.GetAccessibleMoleculeIdsForGrantAsync(currentUserId, "EditChoreTypes"));
        var userCompany = await _db.Companies.Include(c => c.Molecule)
            .FirstOrDefaultAsync(c => c.Id == _companyContext.CompanyId);
        if (userCompany?.MoleculeId.HasValue == true)
            accessibleMoleculeIds.Add(userCompany.MoleculeId.Value);

        return accessibleMoleculeIds.Contains(moleculeId);
    }

    /// <summary>
    /// Sanitizes a color value to a strict #RRGGBB hex format to prevent CSS injection.
    /// Returns null if the input is empty or doesn't match the expected format.
    /// </summary>
    // Delegates to shared utility to avoid drift (2026-04-15 extraction).
    private static string? SanitizeColor(string? color) => ShiftManager.Services.ColorUtilities.SanitizeHexColor(color);
}
