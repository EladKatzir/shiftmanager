using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;

namespace ShiftManager.Pages.Owner;

/// <summary>
/// Blueprints - Manage ShiftTypes (shift definitions) with bilingual names.
/// "Blueprints" are the foundational templates that Programs reference.
/// </summary>
[Authorize(Policy = "IsAdmin")]
public class BlueprintsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ICompanyLocalizationService _localizationService;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<BlueprintsModel> _logger;
    private readonly ITenantResolver _tenantResolver;

    public BlueprintsModel(
        AppDbContext db,
        ICompanyLocalizationService localizationService,
        IAuditLogService auditLogService,
        ILogger<BlueprintsModel> logger,
        ITenantResolver tenantResolver)
    {
        _db = db;
        _localizationService = localizationService;
        _auditLogService = auditLogService;
        _logger = logger;
        _tenantResolver = tenantResolver;
    }

    public List<ShiftType> ShiftTypes { get; set; } = new();
    public string? Success { get; set; }
    public string? Error { get; set; }

    // Create ShiftType form
    [BindProperty] public string NewShiftKey { get; set; } = string.Empty;
    [BindProperty] public string NewShiftNameEn { get; set; } = string.Empty;
    [BindProperty] public string NewShiftNameHe { get; set; } = string.Empty;
    [BindProperty] public TimeOnly NewShiftStart { get; set; } = new TimeOnly(9, 0);
    [BindProperty] public TimeOnly NewShiftEnd { get; set; } = new TimeOnly(17, 0);

    public async Task OnGetAsync(string? success = null, string? error = null)
    {
        Success = success;
        Error = error;

        var companyId = _tenantResolver.GetCurrentTenantId();

        ShiftTypes = await _db.ShiftTypes
            .Where(st => st.CompanyId == companyId)
            .OrderBy(st => st.SortOrder)
            .ToListAsync();
    }

    /// <summary>
    /// Creates a new ShiftType with localized name.
    /// </summary>
    public async Task<IActionResult> OnPostCreateShiftTypeAsync()
    {
        try
        {
            var companyId = _tenantResolver.GetCurrentTenantId();
            var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");

            // Validate
            if (string.IsNullOrWhiteSpace(NewShiftKey))
            {
                return RedirectToPage(new { error = "Shift key is required" });
            }

            if (string.IsNullOrWhiteSpace(NewShiftNameEn) || string.IsNullOrWhiteSpace(NewShiftNameHe))
            {
                return RedirectToPage(new { error = "Both English and Hebrew names are required" });
            }

            // Ensure key is uppercase and doesn't already exist
            NewShiftKey = NewShiftKey.ToUpperInvariant().Replace(" ", "_");
            var keyExists = await _db.ShiftTypes.AnyAsync(st =>
                st.CompanyId == companyId &&
                st.Key == NewShiftKey);

            if (keyExists)
            {
                return RedirectToPage(new { error = $"Shift type with key '{NewShiftKey}' already exists" });
            }

            // Generate NameKey for localization
            var nameKey = $"ShiftType_{NewShiftKey}_Name";

            // Create ShiftType
            var shiftType = new ShiftType
            {
                CompanyId = companyId,
                Key = NewShiftKey,
                NameKey = nameKey,
                Start = NewShiftStart,
                End = NewShiftEnd
            };

            _db.ShiftTypes.Add(shiftType);
            await _db.SaveChangesAsync();

            // Create localization overrides for both cultures
            await _localizationService.UpsertOverrideAsync(
                companyId, "en-US", nameKey, NewShiftNameEn, userId);

            await _localizationService.UpsertOverrideAsync(
                companyId, "he-IL", nameKey, NewShiftNameHe, userId);

            // Audit log

            _logger.LogInformation(
                "Created ShiftType {Key} for Company {CompanyId} by User {UserId}",
                NewShiftKey, companyId, userId);

            return RedirectToPage(new { success = $"Shift type '{NewShiftNameEn}' created successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create ShiftType");
            return RedirectToPage(new { error = "Failed to create shift type" });
        }
    }

    /// <summary>
    /// Updates localized names for an existing ShiftType.
    /// </summary>
    public async Task<IActionResult> OnPostUpdateShiftNameAsync(int shiftTypeId, string nameEn, string nameHe)
    {
        try
        {
            var companyId = _tenantResolver.GetCurrentTenantId();
            var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");

            var shiftType = await _db.ShiftTypes
                .FirstOrDefaultAsync(st => st.Id == shiftTypeId && st.CompanyId == companyId);

            if (shiftType == null)
            {
                return new JsonResult(new { success = false, error = "Shift type not found" });
            }

            if (string.IsNullOrWhiteSpace(shiftType.NameKey))
            {
                return new JsonResult(new { success = false, error = "Shift type has no NameKey" });
            }

            // Update localization overrides
            await _localizationService.UpsertOverrideAsync(
                companyId, "en-US", shiftType.NameKey, nameEn, userId);

            await _localizationService.UpsertOverrideAsync(
                companyId, "he-IL", shiftType.NameKey, nameHe, userId);

            // Audit log

            _logger.LogInformation(
                "Updated ShiftType {Key} names for Company {CompanyId}",
                shiftType.Key, companyId);

            return new JsonResult(new { success = true, message = "Shift name updated" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update shift name");
            return new JsonResult(new { success = false, error = "Failed to update shift name" });
        }
    }

    /// <summary>
    /// Updates time range for a ShiftType.
    /// </summary>
    public async Task<IActionResult> OnPostUpdateShiftTimesAsync(
        int shiftTypeId,
        string startTime,
        string endTime)
    {
        try
        {
            var companyId = _tenantResolver.GetCurrentTenantId();
            var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");

            var shiftType = await _db.ShiftTypes
                .FirstOrDefaultAsync(st => st.Id == shiftTypeId && st.CompanyId == companyId);

            if (shiftType == null)
            {
                return new JsonResult(new { success = false, error = "Shift type not found" });
            }

            // Parse times
            if (!TimeOnly.TryParse(startTime, out var start) || !TimeOnly.TryParse(endTime, out var end))
            {
                return new JsonResult(new { success = false, error = "Invalid time format" });
            }

            shiftType.Start = start;
            shiftType.End = end;

            await _db.SaveChangesAsync();

            // Audit log

            _logger.LogInformation(
                "Updated ShiftType {Key} times for Company {CompanyId}",
                shiftType.Key, companyId);

            return new JsonResult(new { success = true, message = "Shift times updated" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update shift times");
            return new JsonResult(new { success = false, error = "Failed to update shift times" });
        }
    }

    /// <summary>
    /// Deletes a ShiftType if not referenced by Programs.
    /// </summary>
    public async Task<IActionResult> OnPostDeleteShiftTypeAsync(int shiftTypeId)
    {
        try
        {
            var companyId = _tenantResolver.GetCurrentTenantId();
            var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");

            var shiftType = await _db.ShiftTypes
                .FirstOrDefaultAsync(st => st.Id == shiftTypeId && st.CompanyId == companyId);

            if (shiftType == null)
            {
                return RedirectToPage(new { error = "Shift type not found" });
            }

            // Check if used by Programs
            var usedByPrograms = await _db.ShiftPrograms
                .AnyAsync(p => p.ShiftTypeId == shiftTypeId);

            if (usedByPrograms)
            {
                return RedirectToPage(new
                {
                    error = $"Cannot delete '{shiftType.Name}' - it is used by one or more Programs"
                });
            }

            // Check if used by ShiftInstances
            var usedByInstances = await _db.ShiftInstances
                .AnyAsync(si => si.ShiftTypeId == shiftTypeId);

            if (usedByInstances)
            {
                return RedirectToPage(new
                {
                    error = $"Cannot delete '{shiftType.Name}' - it is used by existing shift instances"
                });
            }

            _db.ShiftTypes.Remove(shiftType);
            await _db.SaveChangesAsync();

            // Audit log

            _logger.LogInformation(
                "Deleted ShiftType {Key} from Company {CompanyId}",
                shiftType.Key, companyId);

            return RedirectToPage(new { success = $"Shift type '{shiftType.Name}' deleted" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete ShiftType");
            return RedirectToPage(new { error = "Failed to delete shift type" });
        }
    }
}
