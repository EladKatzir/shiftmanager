using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.Owner;

/// <summary>
/// Blueprints - Manage ShiftTypes (shift definitions) with bilingual names.
/// "Blueprints" are the foundational templates that Programs reference.
/// ✅ P1-1: Expanded access from Owner-only to Manager+Director+Owner
/// </summary>
[Authorize(Policy = "Grant:ManagerHomeAccess")]
public class BlueprintsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ICompanyLocalizationService _localizationService;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<BlueprintsModel> _logger;
    private readonly ITenantResolver _tenantResolver;
    private readonly IConcurrencyService _concurrencyService;
    private readonly IJobTypeService _jobTypeService;
    private readonly IShiftTypeCacheService _shiftTypeCache;

    public BlueprintsModel(
        AppDbContext db,
        ICompanyLocalizationService localizationService,
        IAuditLogService auditLogService,
        ILogger<BlueprintsModel> logger,
        ITenantResolver tenantResolver,
        IConcurrencyService concurrencyService,
        IJobTypeService jobTypeService,
        IShiftTypeCacheService shiftTypeCache)
    {
        _db = db;
        _localizationService = localizationService;
        _auditLogService = auditLogService;
        _logger = logger;
        _tenantResolver = tenantResolver;
        _concurrencyService = concurrencyService;
        _jobTypeService = jobTypeService;
        _shiftTypeCache = shiftTypeCache;
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

    // Molecule scope fields for create form
    [BindProperty] public int? NewShiftMoleculeId { get; set; }
    [BindProperty] public int? NewShiftJobTypeId { get; set; }

    public List<Molecule> AvailableMolecules { get; set; } = new();
    public List<JobType> AvailableJobTypes { get; set; } = new();

    public async Task OnGetAsync(string? success = null, string? error = null)
    {
        Success = success;
        Error = error;

        var companyId = _tenantResolver.GetCurrentTenantId();

        var allShiftTypes = await _db.ShiftTypes
            .Include(st => st.Molecule)
            .Include(st => st.JobType)
            .Where(st => st.CompanyId == companyId)
            .ToListAsync();

        // Sort by SortOrder in memory (it's a [NotMapped] computed property)
        ShiftTypes = allShiftTypes.OrderBy(st => st.SortOrder).ToList();

        // Load molecules for molecule scope selector
        var company = await _db.Companies
            .Include(c => c.Molecule)
            .FirstOrDefaultAsync(c => c.Id == companyId);

        if (company?.MoleculeId != null && company.Molecule?.AreaId != null)
        {
            // Filter to molecules in the same Area as the user's company
            AvailableMolecules = await _db.Molecules
                .Where(m => m.IsActive && m.AreaId == company.Molecule!.AreaId)
                .OrderBy(m => m.DisplayName)
                .ToListAsync();

            AvailableJobTypes = await _jobTypeService.GetJobTypesForMoleculeAsync(company.MoleculeId!.Value);
        }
    }

    /// <summary>
    /// Creates a new ShiftType with localized name.
    /// </summary>
    public async Task<IActionResult> OnPostCreateShiftTypeAsync()
    {
        try
        {
            var companyId = _tenantResolver.GetCurrentTenantId();
            var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : 0;

            // Validate
            if (string.IsNullOrWhiteSpace(NewShiftNameEn) || string.IsNullOrWhiteSpace(NewShiftNameHe))
            {
                return RedirectToPage(new { error = "Both English and Hebrew names are required" });
            }

            // Auto-generate key from English name: "Night Extra" → "CUSTOM_NIGHT_EXTRA"
            NewShiftKey = "CUSTOM_" + System.Text.RegularExpressions.Regex.Replace(
                NewShiftNameEn.Trim(), @"[^a-zA-Z0-9]+", "_").ToUpperInvariant().Trim('_');
            // Ensure unique key — append numeric suffix if needed
            var baseKey = NewShiftKey;
            var suffix = 1;
            while (await _db.ShiftTypes.AnyAsync(st => st.CompanyId == companyId && st.Key == NewShiftKey))
            {
                NewShiftKey = $"{baseKey}_{suffix++}";
            }

            // Validate molecule scope: both or neither must be set
            if (NewShiftMoleculeId.HasValue != NewShiftJobTypeId.HasValue)
            {
                return RedirectToPage(new { error = "Both Molecule and Job Type must be selected together, or leave both empty" });
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
                End = NewShiftEnd,
                MoleculeId = NewShiftMoleculeId,
                JobTypeId = NewShiftJobTypeId
            };

            _db.ShiftTypes.Add(shiftType);
            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "ShiftType");
            if (!saveResult.Success)
                return RedirectToPage(new { error = "A concurrency conflict occurred. Please try again." });

            // Invalidate caches
            _shiftTypeCache.InvalidateCache(companyId);
            if (shiftType.MoleculeId.HasValue && shiftType.JobTypeId.HasValue)
                _shiftTypeCache.InvalidateMoleculeCache(shiftType.MoleculeId.Value, shiftType.JobTypeId.Value);

            // Create localization overrides for both cultures
            await _localizationService.UpsertOverrideAsync(
                companyId, "en-US", nameKey, NewShiftNameEn, userId);

            await _localizationService.UpsertOverrideAsync(
                companyId, "he-IL", nameKey, NewShiftNameHe, userId);

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
            var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : 0;

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

            // Invalidate caches (name changes affect display in calendar views)
            _shiftTypeCache.InvalidateCache(companyId);
            if (shiftType.MoleculeId.HasValue && shiftType.JobTypeId.HasValue)
                _shiftTypeCache.InvalidateMoleculeCache(shiftType.MoleculeId.Value, shiftType.JobTypeId.Value);

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
            var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : 0;

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

            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "ShiftType", shiftTypeId);
            if (!saveResult.Success)
                return new JsonResult(new { success = false, error = saveResult.ErrorMessage }) { StatusCode = 409 };

            // Invalidate caches (time changes affect calendar rendering)
            _shiftTypeCache.InvalidateCache(companyId);
            if (shiftType.MoleculeId.HasValue && shiftType.JobTypeId.HasValue)
                _shiftTypeCache.InvalidateMoleculeCache(shiftType.MoleculeId.Value, shiftType.JobTypeId.Value);

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
            var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : 0;

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

            // Capture scope before deletion for cache invalidation
            var deletedMoleculeId = shiftType.MoleculeId;
            var deletedJobTypeId = shiftType.JobTypeId;

            _db.ShiftTypes.Remove(shiftType);
            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "ShiftType", shiftTypeId);
            if (!saveResult.Success)
                return RedirectToPage(new { error = "A concurrency conflict occurred. Please try again." });

            // Invalidate caches
            _shiftTypeCache.InvalidateCache(companyId);
            if (deletedMoleculeId.HasValue && deletedJobTypeId.HasValue)
                _shiftTypeCache.InvalidateMoleculeCache(deletedMoleculeId.Value, deletedJobTypeId.Value);

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
    /// Publishes an existing ShiftType to a molecule scope by setting MoleculeId and JobTypeId.
    /// </summary>
    public async Task<IActionResult> OnPostPublishToMoleculeAsync(int shiftTypeId, int moleculeId, int jobTypeId)
    {
        try
        {
            var companyId = _tenantResolver.GetCurrentTenantId();

            // Validate molecule exists and is in the same Area as the user's company
            var company = await _db.Companies.Include(c => c.Molecule)
                .FirstOrDefaultAsync(c => c.Id == companyId);
            var molecule = await _db.Molecules.FirstOrDefaultAsync(m => m.Id == moleculeId && m.IsActive);
            if (molecule == null || company?.Molecule?.AreaId != molecule.AreaId)
                return new JsonResult(new { success = false, error = "Invalid molecule for your area" });

            var shiftType = await _db.ShiftTypes
                .FirstOrDefaultAsync(st => st.Id == shiftTypeId && st.CompanyId == companyId);

            if (shiftType == null)
                return new JsonResult(new { success = false, error = "Shift type not found" });

            // Check for duplicate: another ShiftType with the same Key already published to this molecule
            var duplicate = await _db.ShiftTypes
                .IgnoreQueryFilters()
                .AnyAsync(st => st.MoleculeId == moleculeId
                    && st.JobTypeId == jobTypeId
                    && st.Key == shiftType.Key
                    && st.Id != shiftTypeId);

            if (duplicate)
            {
                return new JsonResult(new
                {
                    success = false,
                    error = "A shift type with the same key is already published to this molecule. Duplicate rows will appear in the calendar.",
                    isDuplicate = true
                });
            }

            shiftType.MoleculeId = moleculeId;
            shiftType.JobTypeId = jobTypeId;

            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "ShiftType", shiftTypeId);
            if (!saveResult.Success)
                return new JsonResult(new { success = false, error = saveResult.ErrorMessage }) { StatusCode = 409 };

            // Invalidate caches
            _shiftTypeCache.InvalidateCache(companyId);
            _shiftTypeCache.InvalidateMoleculeCache(moleculeId, jobTypeId);

            _logger.LogInformation(
                "Published ShiftType {Key} to Molecule {MoleculeId} + JobType {JobTypeId}",
                shiftType.Key, moleculeId, jobTypeId);

            return new JsonResult(new { success = true });
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException dbEx)
            when (dbEx.InnerException?.Message.Contains("UNIQUE constraint failed") == true)
        {
            return new JsonResult(new
            {
                success = false,
                error = "A shift type with the same key is already published to this molecule.",
                isDuplicate = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish ShiftType to molecule");
            return new JsonResult(new { success = false, error = "Failed to publish to molecule" });
        }
    }

    /// <summary>
    /// Unpublishes a ShiftType from molecule scope (reverts to company-only).
    /// </summary>
    public async Task<IActionResult> OnPostUnpublishFromMoleculeAsync(int shiftTypeId)
    {
        try
        {
            var companyId = _tenantResolver.GetCurrentTenantId();

            var shiftType = await _db.ShiftTypes
                .FirstOrDefaultAsync(st => st.Id == shiftTypeId && st.CompanyId == companyId);

            if (shiftType == null)
                return new JsonResult(new { success = false, error = "Shift type not found" });

            // Capture old scope for cache invalidation
            var oldMoleculeId = shiftType.MoleculeId;
            var oldJobTypeId = shiftType.JobTypeId;

            shiftType.MoleculeId = null;
            shiftType.JobTypeId = null;

            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "ShiftType", shiftTypeId);
            if (!saveResult.Success)
                return new JsonResult(new { success = false, error = saveResult.ErrorMessage }) { StatusCode = 409 };

            // Invalidate caches
            _shiftTypeCache.InvalidateCache(companyId);
            if (oldMoleculeId.HasValue && oldJobTypeId.HasValue)
                _shiftTypeCache.InvalidateMoleculeCache(oldMoleculeId.Value, oldJobTypeId.Value);

            _logger.LogInformation(
                "Unpublished ShiftType {Key} from molecule scope",
                shiftType.Key);

            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unpublish ShiftType from molecule");
            return new JsonResult(new { success = false, error = "Failed to unpublish from molecule" });
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
            var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : 0;

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
                var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                    () => _db.SaveChangesAsync(), "ShiftType");
                if (!saveResult.Success)
                    return RedirectToPage(new { error = "A concurrency conflict occurred. Please try again." });
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
