using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.Owner;

/// <summary>
/// Blueprints - Manage ShiftTypes (shift definitions) with scope-aware permissions.
/// Supports Company, Molecule, and Area scoped shift types.
/// Grant-based authorization: EditShiftTypes for edit/delete, CreateShiftTypes for create.
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
    private readonly IGrantService _grantService;

    public BlueprintsModel(
        AppDbContext db,
        ICompanyLocalizationService localizationService,
        IAuditLogService auditLogService,
        ILogger<BlueprintsModel> logger,
        ITenantResolver tenantResolver,
        IConcurrencyService concurrencyService,
        IJobTypeService jobTypeService,
        IShiftTypeCacheService shiftTypeCache,
        IGrantService grantService)
    {
        _db = db;
        _localizationService = localizationService;
        _auditLogService = auditLogService;
        _logger = logger;
        _tenantResolver = tenantResolver;
        _concurrencyService = concurrencyService;
        _jobTypeService = jobTypeService;
        _shiftTypeCache = shiftTypeCache;
        _grantService = grantService;
    }

    public List<ShiftType> ShiftTypes { get; set; } = new();
    public string? Success { get; set; }
    public string? Error { get; set; }

    // Molecule selector
    [BindProperty(SupportsGet = true)] public int? SelectedMoleculeId { get; set; }
    public List<Molecule> AvailableMolecules { get; set; } = new();
    public List<JobType> AvailableJobTypes { get; set; } = new();

    // Create ShiftType form
    [BindProperty] public string NewShiftKey { get; set; } = string.Empty;
    [BindProperty] public string NewShiftNameEn { get; set; } = string.Empty;
    [BindProperty] public string NewShiftNameHe { get; set; } = string.Empty;
    [BindProperty] public TimeOnly NewShiftStart { get; set; } = new TimeOnly(9, 0);
    [BindProperty] public TimeOnly NewShiftEnd { get; set; } = new TimeOnly(17, 0);
    [BindProperty] public ShiftScope NewShiftScope { get; set; } = ShiftScope.Molecule;
    [BindProperty] public int? NewShiftMoleculeId { get; set; }
    [BindProperty] public int? NewShiftJobTypeId { get; set; }
    [BindProperty] public int? NewShiftCompanyId { get; set; }

    // Permission helpers
    public bool CanCreateAreaScope { get; set; }
    public bool CanCreateMoleculeScope { get; set; }

    public async Task OnGetAsync(string? success = null, string? error = null)
    {
        Success = success;
        Error = error;

        var companyId = _tenantResolver.GetCurrentTenantId();
        var userId = GetCurrentUserId();

        // Resolve user's molecule and area for scope determination
        var company = await _db.Companies
            .Include(c => c.Molecule)
            .FirstOrDefaultAsync(c => c.Id == companyId);

        if (company?.MoleculeId == null || company.Molecule?.AreaId == null)
            return;

        var areaId = company.Molecule.AreaId;

        // Load available molecules based on user's grants
        AvailableMolecules = await _db.Molecules
            .Where(m => m.IsActive && m.AreaId == areaId)
            .OrderBy(m => m.DisplayName)
            .ToListAsync();

        // Default molecule selection to user's own molecule
        if (!SelectedMoleculeId.HasValue)
            SelectedMoleculeId = company.MoleculeId;

        // Load job types for the selected molecule
        if (SelectedMoleculeId.HasValue)
            AvailableJobTypes = await _jobTypeService.GetJobTypesForMoleculeAsync(SelectedMoleculeId.Value);

        // Scope-aware query: load ALL shifts visible for this molecule
        var query = _db.ShiftTypes
            .Include(st => st.Molecule)
            .Include(st => st.JobType)
            .Include(st => st.Company)
            .Include(st => st.Area)
            .Where(st =>
                (st.MoleculeId == SelectedMoleculeId) ||
                (st.Scope == ShiftScope.Area && st.AreaId == areaId) ||
                (st.Scope == ShiftScope.Company && st.Company != null && st.Company.MoleculeId == SelectedMoleculeId));

        var allShiftTypes = await query.ToListAsync();

        // Sort: Area scope first, then Molecule, then Company. Within each, by SortOrder then Start.
        ShiftTypes = allShiftTypes
            .OrderByDescending(st => (int)st.Scope) // Area(2) first, Molecule(1), Company(0)
            .ThenBy(st => st.SortOrder)
            .ThenBy(st => st.Start)
            .ToList();

        // Determine create permissions
        // Area scope requires ETA-level grant (grant.AreaId or grant.ProjectId must be set)
        // Molecule scope requires ETM-level grant (standard HasGrantWithScopeAsync check)
        if (userId > 0)
        {
            var userGrants = await _grantService.GetUserGrantsAsync(userId);
            var createShiftGrants = userGrants
                .Where(g => g.GrantType?.Key == "CreateShiftTypes")
                .ToList();
            // Area scope: user must have a grant with AreaId or ProjectId set (ETA/ETP level)
            CanCreateAreaScope = createShiftGrants.Any(g => g.AreaId.HasValue || g.ProjectId.HasValue);
            CanCreateMoleculeScope = await _grantService.HasGrantWithScopeAsync(userId, "CreateShiftTypes", moleculeId: SelectedMoleculeId);
        }
    }

    public async Task<IActionResult> OnPostCreateShiftTypeAsync()
    {
        try
        {
            var companyId = _tenantResolver.GetCurrentTenantId();
            var userId = GetCurrentUserId();

            if (string.IsNullOrWhiteSpace(NewShiftNameEn) || string.IsNullOrWhiteSpace(NewShiftNameHe))
                return RedirectToPage(new { error = "Both English and Hebrew names are required" });

            // Resolve scope parameters
            var company = await _db.Companies.Include(c => c.Molecule)
                .FirstOrDefaultAsync(c => c.Id == companyId);
            var areaId = company?.Molecule?.AreaId;

            // Grant check based on scope
            // Area scope requires ETA-level grant (not just molecule-level in the area)
            bool hasGrant;
            if (NewShiftScope == ShiftScope.Area)
            {
                var userGrants = await _grantService.GetUserGrantsAsync(userId);
                hasGrant = userGrants.Any(g => g.GrantType?.Key == "CreateShiftTypes" && (g.AreaId.HasValue || g.ProjectId.HasValue));
            }
            else
            {
                hasGrant = NewShiftScope switch
                {
                    ShiftScope.Molecule => NewShiftMoleculeId.HasValue && await _grantService.HasGrantWithScopeAsync(userId, "CreateShiftTypes", moleculeId: NewShiftMoleculeId),
                    ShiftScope.Company => await _grantService.HasGrantWithScopeAsync(userId, "CreateShiftTypes", companyId: NewShiftCompanyId ?? companyId),
                    _ => false
                };
            }

            if (!hasGrant)
                return RedirectToPage(new { error = "You don't have permission to create shift types at this scope" });

            // Auto-generate key
            NewShiftKey = "CUSTOM_" + System.Text.RegularExpressions.Regex.Replace(
                NewShiftNameEn.Trim(), @"[^a-zA-Z0-9]+", "_").ToUpperInvariant().Trim('_');

            // Ensure unique key within the scope
            var baseKey = NewShiftKey;
            var suffix = 1;
            while (await _db.ShiftTypes.AnyAsync(st => st.MoleculeId == NewShiftMoleculeId && st.Key == NewShiftKey))
            {
                NewShiftKey = $"{baseKey}_{suffix++}";
            }

            var nameKey = $"ShiftType_{NewShiftKey}_Name";

            var shiftType = new ShiftType
            {
                Key = NewShiftKey,
                Scope = NewShiftScope,
                CompanyId = NewShiftScope == ShiftScope.Company ? NewShiftCompanyId : null,
                MoleculeId = NewShiftMoleculeId,
                AreaId = NewShiftScope == ShiftScope.Area ? areaId : null,
                JobTypeId = NewShiftJobTypeId,
                NameKey = nameKey,
                NameEn = NewShiftNameEn.Trim(),
                NameHe = NewShiftNameHe.Trim(),
                Start = NewShiftStart,
                End = NewShiftEnd
            };

            _db.ShiftTypes.Add(shiftType);
            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "ShiftType");
            if (!saveResult.Success)
                return RedirectToPage(new { error = "A concurrency conflict occurred. Please try again." });

            // Invalidate caches
            if (shiftType.MoleculeId.HasValue)
                _shiftTypeCache.InvalidateMoleculeCache(shiftType.MoleculeId.Value, shiftType.JobTypeId);
            if (shiftType.AreaId.HasValue)
                _shiftTypeCache.InvalidateAreaCache(shiftType.AreaId.Value, shiftType.JobTypeId);

            // Store names directly on ShiftType (molecule/area-scoped) + create localization overrides for company-scoped
            if (NewShiftScope == ShiftScope.Company && NewShiftCompanyId.HasValue)
            {
                await _localizationService.UpsertOverrideAsync(NewShiftCompanyId.Value, "en-US", nameKey, NewShiftNameEn, userId);
                await _localizationService.UpsertOverrideAsync(NewShiftCompanyId.Value, "he-IL", nameKey, NewShiftNameHe, userId);
            }

            _logger.LogInformation("Created ShiftType {Key} (Scope={Scope}) by User {UserId}", NewShiftKey, NewShiftScope, userId);
            return RedirectToPage(new { success = $"Shift type '{NewShiftNameEn}' created successfully", selectedMoleculeId = NewShiftMoleculeId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create ShiftType");
            return RedirectToPage(new { error = "Failed to create shift type" });
        }
    }

    public async Task<IActionResult> OnPostUpdateShiftNameAsync(int shiftTypeId, string nameEn, string nameHe)
    {
        try
        {
            var userId = GetCurrentUserId();
            var shiftType = await _db.ShiftTypes.FindAsync(shiftTypeId);
            if (shiftType == null)
                return new JsonResult(new { success = false, error = "Shift type not found" });

            if (!await HasEditGrantForShiftType(userId, shiftType))
                return new JsonResult(new { success = false, error = "Insufficient permissions" }) { StatusCode = 403 };

            // Update names directly on ShiftType
            shiftType.NameEn = nameEn?.Trim();
            shiftType.NameHe = nameHe?.Trim();

            // Also update localization overrides if company-scoped
            if (shiftType.Scope == ShiftScope.Company && shiftType.CompanyId.HasValue && !string.IsNullOrWhiteSpace(shiftType.NameKey))
            {
                await _localizationService.UpsertOverrideAsync(shiftType.CompanyId.Value, "en-US", shiftType.NameKey, nameEn ?? "", userId);
                await _localizationService.UpsertOverrideAsync(shiftType.CompanyId.Value, "he-IL", shiftType.NameKey, nameHe ?? "", userId);
            }

            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "ShiftType", shiftTypeId);
            if (!saveResult.Success)
                return new JsonResult(new { success = false, error = saveResult.ErrorMessage }) { StatusCode = 409 };

            InvalidateCachesForShiftType(shiftType);
            return new JsonResult(new { success = true, message = "Shift name updated" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update shift name");
            return new JsonResult(new { success = false, error = "Failed to update shift name" });
        }
    }

    public async Task<IActionResult> OnPostUpdateShiftTimesAsync(int shiftTypeId, string startTime, string endTime)
    {
        try
        {
            var userId = GetCurrentUserId();
            var shiftType = await _db.ShiftTypes.FindAsync(shiftTypeId);
            if (shiftType == null)
                return new JsonResult(new { success = false, error = "Shift type not found" });

            if (!await HasEditGrantForShiftType(userId, shiftType))
                return new JsonResult(new { success = false, error = "Insufficient permissions" }) { StatusCode = 403 };

            if (!TimeOnly.TryParse(startTime, out var start) || !TimeOnly.TryParse(endTime, out var end))
                return new JsonResult(new { success = false, error = "Invalid time format" });

            shiftType.Start = start;
            shiftType.End = end;

            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "ShiftType", shiftTypeId);
            if (!saveResult.Success)
                return new JsonResult(new { success = false, error = saveResult.ErrorMessage }) { StatusCode = 409 };

            InvalidateCachesForShiftType(shiftType);
            return new JsonResult(new { success = true, message = "Shift times updated" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update shift times");
            return new JsonResult(new { success = false, error = "Failed to update shift times" });
        }
    }

    public async Task<IActionResult> OnGetCheckShiftTypeUsageAsync(int shiftTypeId)
    {
        var shiftType = await _db.ShiftTypes.FindAsync(shiftTypeId);
        if (shiftType == null)
            return new JsonResult(new { error = "Shift type not found" });

        var programCount = await _db.ShiftPrograms.Where(p => p.ShiftTypeId == shiftTypeId).CountAsync();
        var instanceCount = await _db.ShiftInstances.Where(si => si.ShiftTypeId == shiftTypeId).CountAsync();

        return new JsonResult(new { shiftTypeName = shiftType.Name, programCount, instanceCount, canDelete = true });
    }

    public async Task<IActionResult> OnPostDeleteShiftTypeAsync(int shiftTypeId, bool confirmed = false)
    {
        try
        {
            var userId = GetCurrentUserId();
            var shiftType = await _db.ShiftTypes.FindAsync(shiftTypeId);
            if (shiftType == null)
                return RedirectToPage(new { error = "Shift type not found" });

            if (!await HasEditGrantForShiftType(userId, shiftType))
                return RedirectToPage(new { error = "Insufficient permissions" });

            // Check if used by Programs
            if (await _db.ShiftPrograms.AnyAsync(p => p.ShiftTypeId == shiftTypeId))
                return RedirectToPage(new { error = $"Cannot delete '{shiftType.Name}' - it is used by Programs. Remove from Programs first." });

            var instanceCount = await _db.ShiftInstances.Where(si => si.ShiftTypeId == shiftTypeId).CountAsync();
            if (instanceCount > 0 && !confirmed)
                return RedirectToPage(new { error = $"Please confirm deletion of '{shiftType.Name}' ({instanceCount} shift instances)" });

            // Capture for cache invalidation
            var mol = shiftType.MoleculeId;
            var jt = shiftType.JobTypeId;
            var area = shiftType.AreaId;

            _db.ShiftTypes.Remove(shiftType);
            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "ShiftType", shiftTypeId);
            if (!saveResult.Success)
                return RedirectToPage(new { error = "A concurrency conflict occurred." });

            if (mol.HasValue) _shiftTypeCache.InvalidateMoleculeCache(mol.Value, jt);
            if (area.HasValue) _shiftTypeCache.InvalidateAreaCache(area.Value, jt);

            await _auditLogService.LogAsync("ShiftTypeDeleted", "ShiftType", shiftTypeId,
                $"Deleted ShiftType '{shiftType.Name}' (Key: {shiftType.Key}, Scope: {shiftType.Scope}).");

            _logger.LogInformation("Deleted ShiftType {Key} (Scope={Scope}) by User {UserId}", shiftType.Key, shiftType.Scope, userId);
            return RedirectToPage(new { success = $"Shift type '{shiftType.Name}' deleted", selectedMoleculeId = mol });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete ShiftType");
            return RedirectToPage(new { error = "Failed to delete shift type" });
        }
    }

    // --- Helpers ---

    private int GetCurrentUserId()
        => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : 0;

    private async Task<bool> HasEditGrantForShiftType(int userId, ShiftType shiftType)
    {
        return shiftType.Scope switch
        {
            ShiftScope.Area => shiftType.AreaId.HasValue &&
                await _grantService.HasGrantWithScopeAsync(userId, "EditShiftTypes", areaId: shiftType.AreaId),
            ShiftScope.Molecule => shiftType.MoleculeId.HasValue &&
                await _grantService.HasGrantWithScopeAsync(userId, "EditShiftTypes", moleculeId: shiftType.MoleculeId),
            ShiftScope.Company => await _grantService.HasGrantWithScopeAsync(userId, "EditShiftTypes",
                companyId: shiftType.CompanyId ?? _tenantResolver.GetCurrentTenantId()),
            _ => false
        };
    }

    private void InvalidateCachesForShiftType(ShiftType shiftType)
    {
        if (shiftType.CompanyId.HasValue)
            _shiftTypeCache.InvalidateCache(shiftType.CompanyId.Value);
        if (shiftType.MoleculeId.HasValue)
            _shiftTypeCache.InvalidateMoleculeCache(shiftType.MoleculeId.Value, shiftType.JobTypeId);
        if (shiftType.AreaId.HasValue)
            _shiftTypeCache.InvalidateAreaCache(shiftType.AreaId.Value, shiftType.JobTypeId);
    }
}
