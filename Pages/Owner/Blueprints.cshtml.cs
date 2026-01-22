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
/// ✅ P1-1: Expanded access from Owner-only to Manager+Director+Owner
/// </summary>
[Authorize(Policy = "IsManagerOrAdmin")]
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

        var allShiftTypes = await _db.ShiftTypes
            .Where(st => st.CompanyId == companyId)
            .ToListAsync();

        // Sort by SortOrder in memory (it's a [NotMapped] computed property)
        ShiftTypes = allShiftTypes.OrderBy(st => st.SortOrder).ToList();
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
    /// ✅ P1-4: Check ShiftType usage count (for deletion warning modal)
    /// </summary>
    public async Task<IActionResult> OnGetCheckShiftTypeUsageAsync(int shiftTypeId)
    {
        var companyId = _tenantResolver.GetCurrentTenantId();

        var shiftType = await _db.ShiftTypes
            .FirstOrDefaultAsync(st => st.Id == shiftTypeId && st.CompanyId == companyId);

        if (shiftType == null)
        {
            return new JsonResult(new { error = "Shift type not found" });
        }

        // Count usage in Programs
        var programCount = await _db.ShiftPrograms
            .Where(p => p.ShiftTypeId == shiftTypeId)
            .CountAsync();

        // Count usage in ShiftInstances
        var instanceCount = await _db.ShiftInstances
            .Where(si => si.ShiftTypeId == shiftTypeId)
            .CountAsync();

        return new JsonResult(new
        {
            shiftTypeName = shiftType.Name,
            programCount,
            instanceCount,
            canDelete = true // Always allow deletion after confirmation (P1-4 requirement)
        });
    }

    /// <summary>
    /// ✅ P1-4: Deletes a ShiftType with confirmation (allows deletion even if in use)
    /// Deletes a ShiftType if not referenced by Programs.
    /// Allows deletion of ShiftTypes used by ShiftInstances after explicit confirmation.
    /// Historical shifts remain stable (ShiftInstance records are NOT deleted).
    /// </summary>
    public async Task<IActionResult> OnPostDeleteShiftTypeAsync(int shiftTypeId, bool confirmed = false)
    {
        _logger.LogWarning(
            "=====> OnPostDeleteShiftTypeAsync CALLED! ShiftTypeId={ShiftTypeId}, Confirmed={Confirmed}",
            shiftTypeId, confirmed);

        try
        {
            var companyId = _tenantResolver.GetCurrentTenantId();
            var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");

            _logger.LogInformation(
                "DELETE ATTEMPT: ShiftTypeId={ShiftTypeId}, Confirmed={Confirmed}, CompanyId={CompanyId}, UserId={UserId}",
                shiftTypeId, confirmed, companyId, userId);

            var shiftType = await _db.ShiftTypes
                .FirstOrDefaultAsync(st => st.Id == shiftTypeId && st.CompanyId == companyId);

            if (shiftType == null)
            {
                _logger.LogWarning(
                    "DELETE FAILED: ShiftType not found - ShiftTypeId={ShiftTypeId}, CompanyId={CompanyId}",
                    shiftTypeId, companyId);
                return RedirectToPage(new { error = "Shift type not found" });
            }

            _logger.LogInformation(
                "DELETE: Found ShiftType - Id={Id}, Key={Key}, Name={Name}",
                shiftType.Id, shiftType.Key, shiftType.Name);

            // Check if used by Programs (still prevent deletion)
            var usedByPrograms = await _db.ShiftPrograms
                .AnyAsync(p => p.ShiftTypeId == shiftTypeId);

            if (usedByPrograms)
            {
                _logger.LogWarning(
                    "DELETE BLOCKED: ShiftType used by programs - ShiftTypeId={ShiftTypeId}, Key={Key}",
                    shiftTypeId, shiftType.Key);
                return RedirectToPage(new
                {
                    error = $"Cannot delete '{shiftType.Name}' - it is used by one or more Programs. Please remove it from Programs first."
                });
            }

            // ✅ P1-4: Check if used by ShiftInstances (allow deletion with confirmation)
            var instanceCount = await _db.ShiftInstances
                .Where(si => si.ShiftTypeId == shiftTypeId)
                .CountAsync();

            if (instanceCount > 0 && !confirmed)
            {
                // Should not reach here - frontend modal should handle confirmation
                return RedirectToPage(new
                {
                    error = $"Please confirm deletion of '{shiftType.Name}' which is used by {instanceCount} shift instances"
                });
            }

            // ✅ P1-4: Delete the ShiftType (ShiftInstances will keep their ShiftTypeId reference)
            // Note: ShiftInstance records are NOT deleted (historical data preserved)
            _logger.LogInformation(
                "DELETE EXECUTING: Removing ShiftType - ShiftTypeId={ShiftTypeId}, Key={Key}, InstanceCount={InstanceCount}",
                shiftTypeId, shiftType.Key, instanceCount);

            _db.ShiftTypes.Remove(shiftType);
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "DELETE SUCCESS: ShiftType removed from database - ShiftTypeId={ShiftTypeId}, Key={Key}",
                shiftTypeId, shiftType.Key);

            // Audit log
            await _auditLogService.LogAsync(
                "ShiftTypeDeleted",
                "ShiftType",
                shiftTypeId,
                $"Deleted ShiftType '{shiftType.Name}' (Key: {shiftType.Key}). " +
                (instanceCount > 0 ? $"Used by {instanceCount} shift instances (preserved)." : "No shift instances affected."));

            _logger.LogInformation(
                "Deleted ShiftType {Key} from Company {CompanyId}. Used by {InstanceCount} shift instances.",
                shiftType.Key, companyId, instanceCount);

            return RedirectToPage(new { success = $"Shift type '{shiftType.Name}' deleted" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete ShiftType");
            return RedirectToPage(new { error = "Failed to delete shift type" });
        }
    }

    /// <summary>
    /// MIGRATION HELPER: Populates NameKey for existing ShiftTypes that don't have one.
    /// This is a one-time operation after the OpsConsoleScheduler migration.
    /// </summary>
    public async Task<IActionResult> OnPostPopulateNameKeysAsync()
    {
        try
        {
            var companyId = _tenantResolver.GetCurrentTenantId();
            var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");

            var shiftTypes = await _db.ShiftTypes
                .Where(st => st.CompanyId == companyId)
                .ToListAsync();

            int updated = 0;

            foreach (var st in shiftTypes)
            {
                string? nameKey = null;

                // Skip if already has NameKey
                if (!string.IsNullOrWhiteSpace(st.NameKey))
                    continue;

                // Assign NameKey based on Key
                switch (st.Key)
                {
                    case ShiftType.KEY_MORNING:
                        nameKey = "ShiftType_MORNING_Name";
                        break;
                    case ShiftType.KEY_MIDDLE:
                        nameKey = "ShiftType_MIDDLE_Name";
                        break;
                    case ShiftType.KEY_AFTERNOON:
                    case ShiftType.KEY_NOON:
                        nameKey = "ShiftType_AFTERNOON_Name";
                        break;
                    case ShiftType.KEY_NIGHT:
                        nameKey = "ShiftType_NIGHT_Name";
                        break;
                    case ShiftType.KEY_EVENING:
                        nameKey = "ShiftType_EVENING_Name";
                        break;
                    case ShiftType.KEY_OFFLINE:
                        nameKey = "ShiftType_OFFLINE_Name";
                        break;
                    default:
                        // Custom shift type
                        if (st.Key.StartsWith("CUSTOM_"))
                        {
                            nameKey = $"ShiftType_CUSTOM_{st.Id}_Name";
                        }
                        break;
                }

                if (!string.IsNullOrEmpty(nameKey))
                {
                    st.NameKey = nameKey;
                    updated++;
                    _logger.LogInformation(
                        "Populated NameKey for ShiftType {Id} (Key={Key}): {NameKey}",
                        st.Id, st.Key, nameKey);
                }
            }

            if (updated > 0)
            {
                await _db.SaveChangesAsync();
                return RedirectToPage(new { success = $"Populated NameKey for {updated} shift types" });
            }
            else
            {
                return RedirectToPage(new { success = "All shift types already have NameKey values" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to populate NameKeys");
            return RedirectToPage(new { error = "Failed to populate NameKeys" });
        }
    }
}
