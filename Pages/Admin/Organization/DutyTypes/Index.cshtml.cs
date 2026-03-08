using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Pages.Admin.Organization.DutyTypes;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:ManageOnDutyTypes policy;
// OnDutyTypeConfig is a global table (not company-scoped) and OnDuty assignments are global
[Authorize(Policy = "Grant:ManageOnDutyTypes")]
public class IndexModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<IndexModel> _logger;
    private readonly IAuditLogService _auditLogService;

    public IndexModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        ILogger<IndexModel> logger,
        IAuditLogService auditLogService) : base(localizer)
    {
        _db = db;
        _logger = logger;
        _auditLogService = auditLogService;
    }

    // View Models
    public record DutyTypeVM(int Id, int TypeValue, string NameEn, string NameHe, string Icon, string Color, bool RequiresOfficerRank, bool IsActive, bool IsBuiltIn, int AssignmentCount);

    // Data
    public List<DutyTypeVM> DutyTypes { get; set; } = new();

    // Create form
    [BindProperty] public string NameEn { get; set; } = string.Empty;
    [BindProperty] public string NameHe { get; set; } = string.Empty;
    [BindProperty] public string Icon { get; set; } = "\U0001F4CC";
    [BindProperty] public string Color { get; set; } = "#6366f1";
    [BindProperty] public bool RequiresOfficerRank { get; set; }

    // Edit form
    [BindProperty] public int EditId { get; set; }
    [BindProperty] public string EditNameEn { get; set; } = string.Empty;
    [BindProperty] public string EditNameHe { get; set; } = string.Empty;
    [BindProperty] public string EditIcon { get; set; } = string.Empty;
    [BindProperty] public string EditColor { get; set; } = string.Empty;
    [BindProperty] public bool EditRequiresOfficerRank { get; set; }

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
        // SECURITY-AUDITED: IgnoreQueryFilters is safe — OnDuty is a global table (no company filter),
        // and we only aggregate counts grouped by type
        var assignmentCounts = await _db.OnDuties
            .IgnoreQueryFilters()
            .Where(o => o.CanceledAt == null)
            .GroupBy(o => (int)o.Type)
            .Select(g => new { TypeValue = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TypeValue, x => x.Count);

        // Built-in types (always shown, read-only)
        DutyTypes.Add(new DutyTypeVM(0, 0, "Hakam", "\u05D7\u05E7\"\u05DE", "\U0001F46E", "#8B4513", false, true, true, assignmentCounts.GetValueOrDefault(0, 0)));
        DutyTypes.Add(new DutyTypeVM(0, 1, "Lead", "\u05DE\u05D5\u05D1\u05D9\u05DC", "\u2B50", "#1E3A5F", false, true, true, assignmentCounts.GetValueOrDefault(1, 0)));

        // Custom types from DB
        // SECURITY-AUDITED: IgnoreQueryFilters is safe — OnDutyTypeConfig is a global table (no company query filter)
        var customTypes = await _db.OnDutyTypeConfigs
            .IgnoreQueryFilters()
            .OrderBy(t => t.TypeValue)
            .ToListAsync();

        foreach (var ct in customTypes)
        {
            DutyTypes.Add(new DutyTypeVM(
                ct.Id,
                ct.TypeValue,
                ct.NameEn,
                ct.NameHe,
                ct.Icon,
                ct.Color,
                ct.RequiresOfficerRank,
                ct.IsActive,
                false,
                assignmentCounts.GetValueOrDefault(ct.TypeValue, 0)
            ));
        }
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (string.IsNullOrWhiteSpace(NameEn) || NameEn.Length > 100)
        {
            TempData["ErrorMessage"] = _localizer["Error_NameEnRequired"].Value;
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(NameHe) || NameHe.Length > 100)
        {
            TempData["ErrorMessage"] = _localizer["Error_NameHeRequired"].Value;
            return RedirectToPage();
        }

        // Auto-increment TypeValue: max existing + 1, minimum 2
        // SECURITY-AUDITED: IgnoreQueryFilters is safe — OnDutyTypeConfig is a global table
        var maxTypeValue = await _db.OnDutyTypeConfigs
            .IgnoreQueryFilters()
            .Select(t => (int?)t.TypeValue)
            .MaxAsync() ?? 1;
        var newTypeValue = Math.Max(2, maxTypeValue + 1);

        var userId = 0;
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userIdClaim))
            int.TryParse(userIdClaim, out userId);

        var config = new OnDutyTypeConfig
        {
            TypeValue = newTypeValue,
            NameEn = NameEn.Trim(),
            NameHe = NameHe.Trim(),
            Icon = string.IsNullOrWhiteSpace(Icon) ? "\U0001F4CC" : Icon.Trim(),
            Color = SanitizeColor(Color) ?? "#6366f1",
            RequiresOfficerRank = RequiresOfficerRank,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userId
        };

        _db.OnDutyTypeConfigs.Add(config);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Created OnDutyTypeConfig {Id}: {NameEn} (TypeValue={TypeValue}) by User {UserId}",
            config.Id, config.NameEn, config.TypeValue, userId);

        await _auditLogService.LogUserActionAsync(userId, "Create", "OnDutyTypeConfig", config.Id,
            $"Created duty type '{config.NameEn}' (TypeValue={config.TypeValue})");

        TempData["SuccessMessage"] = string.Format(_localizer["Success_DutyTypeCreated"].Value, config.NameEn);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync()
    {
        if (EditId <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_DutyTypeNotFound"].Value;
            return RedirectToPage();
        }

        // SECURITY-AUDITED: IgnoreQueryFilters is safe — OnDutyTypeConfig is a global table
        var config = await _db.OnDutyTypeConfigs
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == EditId);

        if (config == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_DutyTypeNotFound"].Value;
            return RedirectToPage();
        }

        // Verify it's a custom type (TypeValue >= 2)
        if (config.TypeValue < 2)
        {
            TempData["ErrorMessage"] = _localizer["Error_CannotEditBuiltInType"].Value;
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(EditNameEn) || EditNameEn.Length > 100)
        {
            TempData["ErrorMessage"] = _localizer["Error_NameEnRequired"].Value;
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(EditNameHe) || EditNameHe.Length > 100)
        {
            TempData["ErrorMessage"] = _localizer["Error_NameHeRequired"].Value;
            return RedirectToPage();
        }

        var oldName = config.NameEn;
        config.NameEn = EditNameEn.Trim();
        config.NameHe = EditNameHe.Trim();
        config.Icon = string.IsNullOrWhiteSpace(EditIcon) ? config.Icon : EditIcon.Trim();
        config.Color = SanitizeColor(EditColor) ?? config.Color;
        config.RequiresOfficerRank = EditRequiresOfficerRank;

        await _db.SaveChangesAsync();

        var userId = 0;
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userIdClaim))
            int.TryParse(userIdClaim, out userId);

        _logger.LogInformation("Updated OnDutyTypeConfig {Id}: {OldName} -> {NewName} by User {UserId}",
            config.Id, oldName, config.NameEn, userId);

        await _auditLogService.LogUserActionAsync(userId, "Update", "OnDutyTypeConfig", config.Id,
            $"Updated duty type '{config.NameEn}' (TypeValue={config.TypeValue})");

        TempData["SuccessMessage"] = string.Format(_localizer["Success_DutyTypeUpdated"].Value, config.NameEn);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(int id)
    {
        // SECURITY-AUDITED: IgnoreQueryFilters is safe — OnDutyTypeConfig is a global table
        var config = await _db.OnDutyTypeConfigs
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == id);

        if (config == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_DutyTypeNotFound"].Value;
            return RedirectToPage();
        }

        if (config.TypeValue < 2)
        {
            TempData["ErrorMessage"] = _localizer["Error_CannotEditBuiltInType"].Value;
            return RedirectToPage();
        }

        config.IsActive = !config.IsActive;
        await _db.SaveChangesAsync();

        var userId = 0;
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userIdClaim))
            int.TryParse(userIdClaim, out userId);

        _logger.LogInformation("OnDutyTypeConfig {Id} ({Name}) active status changed to {IsActive} by User {UserId}",
            id, config.NameEn, config.IsActive, userId);

        await _auditLogService.LogUserActionAsync(userId, "ToggleActive", "OnDutyTypeConfig", config.Id,
            $"Duty type '{config.NameEn}' {(config.IsActive ? "activated" : "deactivated")}");

        TempData["SuccessMessage"] = config.IsActive
            ? string.Format(_localizer["Success_DutyTypeActivated"].Value, config.NameEn)
            : string.Format(_localizer["Success_DutyTypeDeactivated"].Value, config.NameEn);

        return RedirectToPage();
    }

    /// <summary>
    /// Sanitizes a color value to a strict #RRGGBB hex format to prevent CSS injection.
    /// Returns null if the input is empty or doesn't match the expected format.
    /// </summary>
    private static string? SanitizeColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return null;
        var trimmed = color.Trim();
        return Regex.IsMatch(trimmed, @"^#[0-9A-Fa-f]{6}$") ? trimmed : null;
    }
}
