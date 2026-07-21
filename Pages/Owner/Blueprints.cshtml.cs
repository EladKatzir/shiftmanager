using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;

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
    private readonly IShiftCategoryService _categoryService;

    public BlueprintsModel(
        AppDbContext db,
        ICompanyLocalizationService localizationService,
        IAuditLogService auditLogService,
        ILogger<BlueprintsModel> logger,
        ITenantResolver tenantResolver,
        IConcurrencyService concurrencyService,
        IJobTypeService jobTypeService,
        IShiftTypeCacheService shiftTypeCache,
        IGrantService grantService,
        IShiftCategoryService categoryService)
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
        _categoryService = categoryService;
    }

    public List<ShiftType> ShiftTypes { get; set; } = new();

    /// <summary>
    /// Shift types (within the current molecule view) whose NameKey is missing. Surfaced so the
    /// banner can list exactly which items are affected and the inline "Add name keys" modal can
    /// preview the keys that will be assigned.
    /// </summary>
    public List<ShiftType> MissingNameKeyShifts { get; set; } = new();
    // Success / Error properties removed — feedback now flows through TempData → _Layout FeedbackModal bridge.

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

    // Shift categories (functional groupings, e.g. "Yekev") for the selected molecule.
    public List<ShiftCategory> Categories { get; set; } = new();

    // Create-category form
    [BindProperty] public string NewCategoryName { get; set; } = string.Empty;
    [BindProperty] public string? NewCategoryColor { get; set; }

    // Permission helpers
    public bool CanCreateAreaScope { get; set; }
    public bool CanCreateMoleculeScope { get; set; }
    public bool CanManageCategories { get; set; }

    public async Task OnGetAsync()
    {
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

        // Items missing a NameKey — drives the banner list + the inline add-keys modal preview.
        MissingNameKeyShifts = ShiftTypes
            .Where(st => string.IsNullOrWhiteSpace(st.NameKey))
            .ToList();

        // Shift categories for the selected molecule + whether the user may manage them.
        if (SelectedMoleculeId.HasValue)
        {
            Categories = await _categoryService.GetCategoriesForMoleculeAsync(SelectedMoleculeId.Value);
            CanManageCategories = userId > 0 &&
                await _grantService.HasGrantWithScopeAsync(userId, "ManageShiftCategories", moleculeId: SelectedMoleculeId);
        }

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
            // Molecule scope: any CreateShiftTypes grant is sufficient — the specific molecule is validated during creation
            CanCreateMoleculeScope = createShiftGrants.Any();
        }
    }

    public async Task<IActionResult> OnPostCreateShiftTypeAsync()
    {
        try
        {
            var companyId = _tenantResolver.GetCurrentTenantId();
            var userId = GetCurrentUserId();

            if (string.IsNullOrWhiteSpace(NewShiftNameEn) || string.IsNullOrWhiteSpace(NewShiftNameHe))
            {
                TempData["ErrorMessage"] = "Both English and Hebrew names are required";
                TempData["ErrorId"] = HttpContext.TraceIdentifier;
                return RedirectToPage();
            }

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
            {
                TempData["ErrorMessage"] = "You don't have permission to create shift types at this scope";
                TempData["ErrorId"] = HttpContext.TraceIdentifier;
                return RedirectToPage();
            }

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
            {
                TempData["ErrorMessage"] = "A concurrency conflict occurred. Please try again.";
                TempData["ErrorId"] = HttpContext.TraceIdentifier;
                return RedirectToPage();
            }

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
            TempData["SuccessMessage"] = $"Shift type '{NewShiftNameEn}' created successfully";
            return RedirectToPage(new { selectedMoleculeId = NewShiftMoleculeId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create ShiftType");
            TempData["ErrorMessage"] = "Failed to create shift type";
            TempData["ErrorId"] = HttpContext.TraceIdentifier;
            return RedirectToPage();
        }
    }

    public async Task<IActionResult> OnPostUpdateShiftNameAsync([FromBody] UpdateShiftNameRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            var shiftType = await _db.ShiftTypes.FindAsync(request.ShiftTypeId);
            if (shiftType == null)
                return new JsonResult(new { success = false, error = "Shift type not found" });

            var (allowed, errorMessage) = await CheckEditGrantForShiftTypeAsync(userId, shiftType);
            if (!allowed)
                return new JsonResult(new { success = false, error = errorMessage }) { StatusCode = 403 };

            // Update names directly on ShiftType
            shiftType.NameEn = request.NameEn?.Trim();
            shiftType.NameHe = request.NameHe?.Trim();

            // Also update localization overrides if company-scoped
            if (shiftType.Scope == ShiftScope.Company && shiftType.CompanyId.HasValue && !string.IsNullOrWhiteSpace(shiftType.NameKey))
            {
                await _localizationService.UpsertOverrideAsync(shiftType.CompanyId.Value, "en-US", shiftType.NameKey, request.NameEn ?? "", userId);
                await _localizationService.UpsertOverrideAsync(shiftType.CompanyId.Value, "he-IL", shiftType.NameKey, request.NameHe ?? "", userId);
            }

            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "ShiftType", request.ShiftTypeId);
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

    public async Task<IActionResult> OnPostUpdateShiftTimesAsync([FromBody] UpdateShiftTimesRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            var shiftType = await _db.ShiftTypes.FindAsync(request.ShiftTypeId);
            if (shiftType == null)
                return new JsonResult(new { success = false, error = "Shift type not found" });

            var (allowed, errorMessage) = await CheckEditGrantForShiftTypeAsync(userId, shiftType);
            if (!allowed)
                return new JsonResult(new { success = false, error = errorMessage }) { StatusCode = 403 };

            if (!TimeOnly.TryParse(request.StartTime, out var start) || !TimeOnly.TryParse(request.EndTime, out var end))
                return new JsonResult(new { success = false, error = "Invalid time format" });

            shiftType.Start = start;
            shiftType.End = end;

            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "ShiftType", request.ShiftTypeId);
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

    /// <summary>
    /// Issue 1: toggle whether a shift type blocks overlapping/back-to-back shifts and whether its
    /// hours count toward the weekly cap and analytics. Presence statuses (Offline/Home) are
    /// intrinsically exempt regardless of these columns, so the view disables the toggles for them.
    /// </summary>
    public async Task<IActionResult> OnPostUpdateShiftFlagsAsync([FromBody] UpdateShiftFlagsRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            var shiftType = await _db.ShiftTypes.FindAsync(request.ShiftTypeId);
            if (shiftType == null)
                return new JsonResult(new { success = false, error = "Shift type not found" });

            var (allowed, errorMessage) = await CheckEditGrantForShiftTypeAsync(userId, shiftType);
            if (!allowed)
                return new JsonResult(new { success = false, error = errorMessage }) { StatusCode = 403 };

            shiftType.IsBlocking = request.IsBlocking;
            shiftType.CountsTowardHourLimits = request.CountsTowardHourLimits;

            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "ShiftType", request.ShiftTypeId);
            if (!saveResult.Success)
                return new JsonResult(new { success = false, error = saveResult.ErrorMessage }) { StatusCode = 409 };

            InvalidateCachesForShiftType(shiftType);
            return new JsonResult(new { success = true, message = "Shift settings updated" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update shift flags");
            return new JsonResult(new { success = false, error = "Failed to update shift settings" });
        }
    }

    /// <summary>
    /// Inline replacement for the old (broken) "PopulateNameKeys" form handler. Assigns the canonical
    /// <c>ShiftType_{Key}_Name</c> NameKey to every shift type that is currently missing one — the same
    /// convention <see cref="OnPostCreateShiftTypeAsync"/> uses for new shift types. Idempotent: a
    /// shift type that already has a NameKey is left untouched. Returns JSON so the page can stay inline
    /// (fetch + reload) instead of a full-page redirect.
    /// </summary>
    public async Task<IActionResult> OnPostAddNamekeysAsync()
    {
        try
        {
            var userId = GetCurrentUserId();

            // Match the view/banner predicate exactly (IsNullOrWhiteSpace is not reliably SQL-translatable,
            // and the ShiftTypes table is small) — load then filter in memory.
            var all = await _db.ShiftTypes.ToListAsync();
            var missing = all.Where(st => string.IsNullOrWhiteSpace(st.NameKey)).ToList();

            if (missing.Count == 0)
                return new JsonResult(new { success = true, count = 0 });

            foreach (var st in missing)
                st.NameKey = $"ShiftType_{st.Key}_Name";

            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "ShiftType");
            if (!saveResult.Success)
                return new JsonResult(new { success = false, error = saveResult.ErrorMessage }) { StatusCode = 409 };

            // Invalidate caches for every affected scope so the resolved names refresh.
            foreach (var st in missing)
            {
                if (st.CompanyId.HasValue) _shiftTypeCache.InvalidateCache(st.CompanyId.Value);
                if (st.MoleculeId.HasValue) _shiftTypeCache.InvalidateMoleculeCache(st.MoleculeId.Value, st.JobTypeId);
                if (st.AreaId.HasValue) _shiftTypeCache.InvalidateAreaCache(st.AreaId.Value, st.JobTypeId);
            }

            await _auditLogService.LogAsync("ShiftTypeNameKeysPopulated", "ShiftType", 0,
                $"Populated NameKey for {missing.Count} shift type(s) missing one.");
            _logger.LogInformation("Populated NameKeys for {Count} shift types by User {UserId}", missing.Count, userId);

            return new JsonResult(new { success = true, count = missing.Count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to populate NameKeys");
            return new JsonResult(new { success = false, error = "Failed to populate name keys" }) { StatusCode = 500 };
        }
    }

    public async Task<IActionResult> OnGetCheckShiftTypeUsageAsync(int shiftTypeId)
    {
        var shiftType = await _db.ShiftTypes.FindAsync(shiftTypeId);
        if (shiftType == null)
            return new JsonResult(new { error = "Shift type not found" });

        var programCount = await _db.ShiftPrograms.Where(p => p.ShiftTypeId == shiftTypeId).CountAsync();
        var instanceCount = await _db.ShiftInstances.Where(si => si.ShiftTypeId == shiftTypeId).CountAsync();

        var companyId = _tenantResolver.GetCurrentTenantId();
        var localizedName = await _localizationService.ResolveShiftTypeNameAsync(shiftType, companyId, CultureInfo.CurrentUICulture.Name);
        return new JsonResult(new { shiftTypeName = localizedName, programCount, instanceCount, canDelete = true });
    }

    public async Task<IActionResult> OnPostDeleteShiftTypeAsync(int shiftTypeId, bool confirmed = false)
    {
        try
        {
            var userId = GetCurrentUserId();
            var shiftType = await _db.ShiftTypes.FindAsync(shiftTypeId);
            if (shiftType == null)
            {
                TempData["ErrorMessage"] = "Shift type not found";
                TempData["ErrorId"] = HttpContext.TraceIdentifier;
                return RedirectToPage();
            }

            var (allowed, errorMessage) = await CheckEditGrantForShiftTypeAsync(userId, shiftType);
            if (!allowed)
            {
                TempData["ErrorMessage"] = errorMessage;
                TempData["ErrorId"] = HttpContext.TraceIdentifier;
                return RedirectToPage();
            }

            var companyId = _tenantResolver.GetCurrentTenantId();
            var localizedName = await _localizationService.ResolveShiftTypeNameAsync(shiftType, companyId, CultureInfo.CurrentUICulture.Name);

            // Check if used by Programs
            if (await _db.ShiftPrograms.AnyAsync(p => p.ShiftTypeId == shiftTypeId))
            {
                TempData["ErrorMessage"] = $"Cannot delete '{localizedName}' - it is used by Programs. Remove from Programs first.";
                TempData["ErrorId"] = HttpContext.TraceIdentifier;
                return RedirectToPage();
            }

            var instanceCount = await _db.ShiftInstances.Where(si => si.ShiftTypeId == shiftTypeId).CountAsync();
            if (instanceCount > 0 && !confirmed)
            {
                TempData["ErrorMessage"] = $"Please confirm deletion of '{localizedName}' ({instanceCount} shift instances)";
                TempData["ErrorId"] = HttpContext.TraceIdentifier;
                return RedirectToPage();
            }

            // Capture for cache invalidation
            var mol = shiftType.MoleculeId;
            var jt = shiftType.JobTypeId;
            var area = shiftType.AreaId;

            _db.ShiftTypes.Remove(shiftType);
            var saveResult = await _concurrencyService.SaveWithConcurrencyHandlingAsync(
                () => _db.SaveChangesAsync(), "ShiftType", shiftTypeId);
            if (!saveResult.Success)
            {
                TempData["ErrorMessage"] = "A concurrency conflict occurred.";
                TempData["ErrorId"] = HttpContext.TraceIdentifier;
                return RedirectToPage();
            }

            if (mol.HasValue) _shiftTypeCache.InvalidateMoleculeCache(mol.Value, jt);
            if (area.HasValue) _shiftTypeCache.InvalidateAreaCache(area.Value, jt);

            await _auditLogService.LogAsync("ShiftTypeDeleted", "ShiftType", shiftTypeId,
                $"Deleted ShiftType '{shiftType.Key}' (Scope: {shiftType.Scope}).");

            _logger.LogInformation("Deleted ShiftType {Key} (Scope={Scope}) by User {UserId}", shiftType.Key, shiftType.Scope, userId);
            TempData["SuccessMessage"] = $"Shift type '{localizedName}' deleted";
            return RedirectToPage(new { selectedMoleculeId = mol });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete ShiftType");
            TempData["ErrorMessage"] = "Failed to delete shift type";
            TempData["ErrorId"] = HttpContext.TraceIdentifier;
            return RedirectToPage();
        }
    }

    // --- Shift Category handlers (gated by ManageShiftCategories on the target molecule) ---

    public async Task<IActionResult> OnPostCreateCategoryAsync(int moleculeId)
    {
        var userId = GetCurrentUserId();
        if (!await CanManageCategoriesAsync(userId, moleculeId))
        {
            TempData["ErrorMessage"] = InsufficientPermissionsMessage();
            TempData["ErrorId"] = HttpContext.TraceIdentifier;
            return RedirectToPage(new { selectedMoleculeId = moleculeId });
        }

        var name = (NewCategoryName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["ErrorMessage"] = IsHebrewUi() ? "יש להזין שם קטגוריה" : "Category name is required";
            TempData["ErrorId"] = HttpContext.TraceIdentifier;
            return RedirectToPage(new { selectedMoleculeId = moleculeId });
        }

        var created = await _categoryService.CreateAsync(moleculeId, name, name, NewCategoryColor);
        if (created == null)
        {
            TempData["ErrorMessage"] = IsHebrewUi()
                ? $"כבר קיימת קטגוריה בשם '{name}' במולקולה זו"
                : $"A category named '{name}' already exists in this molecule";
            TempData["ErrorId"] = HttpContext.TraceIdentifier;
            return RedirectToPage(new { selectedMoleculeId = moleculeId });
        }

        await _auditLogService.LogAsync("ShiftCategoryCreated", "ShiftCategory", created.Id,
            $"Created shift category '{created.Name}' in molecule {moleculeId}.");
        _logger.LogInformation("Created ShiftCategory {Id} '{Name}' in molecule {MoleculeId} by User {UserId}",
            created.Id, created.Name, moleculeId, userId);
        TempData["SuccessMessage"] = IsHebrewUi() ? $"הקטגוריה '{name}' נוצרה" : $"Category '{name}' created";
        return RedirectToPage(new { selectedMoleculeId = moleculeId });
    }

    public async Task<IActionResult> OnPostRenameCategoryAsync([FromBody] RenameCategoryRequest request)
    {
        var userId = GetCurrentUserId();
        var category = await _categoryService.GetCategoryAsync(request.CategoryId);
        if (category == null)
            return new JsonResult(new { success = false, error = "Category not found" });
        if (!await CanManageCategoriesAsync(userId, category.MoleculeId))
            return new JsonResult(new { success = false, error = InsufficientPermissionsMessage() }) { StatusCode = 403 };

        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            return new JsonResult(new { success = false, error = "Category name is required" });

        var ok = await _categoryService.RenameAsync(category.Id, name, name, request.Color);
        if (!ok)
            return new JsonResult(new { success = false, error = "Rename failed (duplicate name?)" }) { StatusCode = 409 };

        await _auditLogService.LogAsync("ShiftCategoryRenamed", "ShiftCategory", category.Id,
            $"Renamed shift category {category.Id} to '{name}'.");
        return new JsonResult(new { success = true });
    }

    public async Task<IActionResult> OnGetCheckCategoryUsageAsync(int categoryId)
    {
        var category = await _categoryService.GetCategoryAsync(categoryId);
        if (category == null)
            return new JsonResult(new { error = "Category not found" });

        var (shiftTypeCount, memberCount) = await _categoryService.GetUsageAsync(categoryId);
        return new JsonResult(new { categoryName = category.DisplayName, shiftTypeCount, memberCount });
    }

    public async Task<IActionResult> OnPostDeleteCategoryAsync(int categoryId, bool confirmed = false)
    {
        var userId = GetCurrentUserId();
        var category = await _categoryService.GetCategoryAsync(categoryId);
        if (category == null)
        {
            TempData["ErrorMessage"] = IsHebrewUi() ? "הקטגוריה לא נמצאה" : "Category not found";
            TempData["ErrorId"] = HttpContext.TraceIdentifier;
            return RedirectToPage();
        }

        var moleculeId = category.MoleculeId;
        if (!await CanManageCategoriesAsync(userId, moleculeId))
        {
            TempData["ErrorMessage"] = InsufficientPermissionsMessage();
            TempData["ErrorId"] = HttpContext.TraceIdentifier;
            return RedirectToPage(new { selectedMoleculeId = moleculeId });
        }

        var (shiftTypeCount, memberCount) = await _categoryService.GetUsageAsync(categoryId);
        if ((shiftTypeCount > 0 || memberCount > 0) && !confirmed)
        {
            TempData["ErrorMessage"] = IsHebrewUi()
                ? $"הקטגוריה '{category.DisplayName}' בשימוש ({shiftTypeCount} משמרות, {memberCount} משתמשים). יש לאשר מחיקה."
                : $"Category '{category.DisplayName}' is in use ({shiftTypeCount} shifts, {memberCount} users). Please confirm deletion.";
            TempData["ErrorId"] = HttpContext.TraceIdentifier;
            return RedirectToPage(new { selectedMoleculeId = moleculeId });
        }

        await _categoryService.DeleteAsync(categoryId);
        await _auditLogService.LogAsync("ShiftCategoryDeleted", "ShiftCategory", categoryId,
            $"Deleted shift category '{category.Name}' (unmapped {shiftTypeCount} shifts, {memberCount} members).");
        _logger.LogInformation("Deleted ShiftCategory {Id} '{Name}' by User {UserId}", categoryId, category.Name, userId);
        TempData["SuccessMessage"] = IsHebrewUi() ? $"הקטגוריה '{category.DisplayName}' נמחקה" : $"Category '{category.DisplayName}' deleted";
        return RedirectToPage(new { selectedMoleculeId = moleculeId });
    }

    public async Task<IActionResult> OnPostAssignShiftCategoryAsync([FromBody] AssignShiftCategoryRequest request)
    {
        var userId = GetCurrentUserId();
        var shiftType = await _db.ShiftTypes.IgnoreQueryFilters() // SECURITY-AUDITED: shift types are not tenant-filtered
            .FirstOrDefaultAsync(st => st.Id == request.ShiftTypeId);
        if (shiftType == null)
            return new JsonResult(new { success = false, error = "Shift type not found" });

        // The molecule the shift resolves to gates the grant (direct, or via its company).
        var shiftMoleculeId = shiftType.MoleculeId
            ?? await _db.Companies.IgnoreQueryFilters()
                .Where(c => c.Id == shiftType.CompanyId).Select(c => c.MoleculeId).FirstOrDefaultAsync();
        if (shiftMoleculeId == null || !await CanManageCategoriesAsync(userId, shiftMoleculeId.Value))
            return new JsonResult(new { success = false, error = InsufficientPermissionsMessage() }) { StatusCode = 403 };

        var ok = await _categoryService.AssignShiftTypeAsync(request.ShiftTypeId, request.CategoryId);
        if (!ok)
            return new JsonResult(new { success = false, error = "Category must belong to the shift's molecule" }) { StatusCode = 400 };

        if (shiftType.MoleculeId.HasValue)
            _shiftTypeCache.InvalidateMoleculeCache(shiftType.MoleculeId.Value, shiftType.JobTypeId);
        await _auditLogService.LogAsync("ShiftTypeCategoryAssigned", "ShiftType", request.ShiftTypeId,
            $"Set ShiftType {request.ShiftTypeId} category to {(request.CategoryId?.ToString() ?? "none")}.");
        return new JsonResult(new { success = true });
    }

    private Task<bool> CanManageCategoriesAsync(int userId, int moleculeId)
        => userId <= 0
            ? Task.FromResult(false)
            : _grantService.HasGrantWithScopeAsync(userId, "ManageShiftCategories", moleculeId: moleculeId);

    // --- Helpers ---

    private int GetCurrentUserId()
        => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : 0;

    /// <summary>
     /// Authorization check for editing a ShiftType. Returns (allowed, errorMessage).
    /// Two-step logic:
    ///   1. Scope check — user must have an EditShiftTypes grant that covers the shift's scope
    ///      (Area / Molecule / Company). Fails => SCOPE error.
    ///   2. JobType check — for shifts with a specific JobTypeId, the user must hold at least one
    ///      EditShiftTypes grant whose JobTypeId is null (cross-JobType, e.g. קב"ר) OR equals the
    ///      shift's JobTypeId. Global shifts (ShiftType.JobTypeId == null) bypass this step so any
    ///      role with the scope grant can edit them. Fails => JOBTYPE error carrying the shift's
    ///      JobType name for a "This isn't a {JobType} shift" message.
    /// </summary>
    private async Task<(bool Allowed, string? ErrorMessage)> CheckEditGrantForShiftTypeAsync(int userId, ShiftType shiftType)
    {
        bool hasScopeGrant = shiftType.Scope switch
        {
            ShiftScope.Area => shiftType.AreaId.HasValue &&
                await _grantService.HasGrantWithScopeAsync(userId, "EditShiftTypes", areaId: shiftType.AreaId),
            ShiftScope.Molecule => shiftType.MoleculeId.HasValue &&
                await _grantService.HasGrantWithScopeAsync(userId, "EditShiftTypes", moleculeId: shiftType.MoleculeId),
            ShiftScope.Company => await _grantService.HasGrantWithScopeAsync(userId, "EditShiftTypes",
                companyId: shiftType.CompanyId ?? _tenantResolver.GetCurrentTenantId()),
            _ => false
        };

        if (!hasScopeGrant)
            return (false, InsufficientPermissionsMessage());

        // Global shifts (no JobType binding) — any holder of the scope grant may edit
        if (!shiftType.JobTypeId.HasValue)
            return (true, null);

        // JobType-specific shift — look up the user's EditShiftTypes grants and verify at least one
        // covers this JobType (either cross-JobType via JobTypeId=null, or an exact match).
        // Note: HasGrantWithScopeAsync's Molecule/Area/Project branches do not enforce grant.JobTypeId,
        // so this explicit check is where useOwnJobType actually gates behavior for Lead/Director.
        var grantType = await _grantService.GetGrantTypeByKeyAsync("EditShiftTypes");
        if (grantType == null)
            return (false, InsufficientPermissionsMessage());

        var userGrants = await _grantService.GetUserGrantsByTypeAsync(userId, grantType.Id);
        bool hasJobTypeAccess = userGrants.Any(g => g.CanOwn &&
            (!g.JobTypeId.HasValue || g.JobTypeId == shiftType.JobTypeId));
        if (hasJobTypeAccess)
            return (true, null);

        var jobType = await _db.JobTypes.FirstOrDefaultAsync(jt => jt.Id == shiftType.JobTypeId);
        var name = !string.IsNullOrWhiteSpace(jobType?.DisplayName) ? jobType!.DisplayName
                 : !string.IsNullOrWhiteSpace(jobType?.Name) ? jobType!.Name
                 : (IsHebrewUi() ? "זה" : "this");
        var msg = IsHebrewUi()
            ? $"זו אינה משמרת {name} — אין לך הרשאה לערוך משמרות מסוג אחר"
            : $"This isn't a {name} shift — you don't have permission to edit shifts of a different JobType";
        return (false, msg);
    }

    private static string InsufficientPermissionsMessage()
        => IsHebrewUi() ? "אין לך הרשאה לערוך את סוג המשמרת הזה" : "Insufficient permissions";

    private static bool IsHebrewUi()
        => CultureInfo.CurrentUICulture.Name.StartsWith("he", StringComparison.OrdinalIgnoreCase);

    private void InvalidateCachesForShiftType(ShiftType shiftType)
    {
        if (shiftType.CompanyId.HasValue)
            _shiftTypeCache.InvalidateCache(shiftType.CompanyId.Value);
        if (shiftType.MoleculeId.HasValue)
            _shiftTypeCache.InvalidateMoleculeCache(shiftType.MoleculeId.Value, shiftType.JobTypeId);
        if (shiftType.AreaId.HasValue)
            _shiftTypeCache.InvalidateAreaCache(shiftType.AreaId.Value, shiftType.JobTypeId);
    }

    // --- Request DTOs for JSON-body binding ---

    public class UpdateShiftNameRequest
    {
        public int ShiftTypeId { get; set; }
        public string NameEn { get; set; } = string.Empty;
        public string NameHe { get; set; } = string.Empty;
    }

    public class UpdateShiftTimesRequest
    {
        public int ShiftTypeId { get; set; }
        public string StartTime { get; set; } = string.Empty;
        public string EndTime { get; set; } = string.Empty;
    }

    public class UpdateShiftFlagsRequest
    {
        public int ShiftTypeId { get; set; }
        public bool IsBlocking { get; set; } = true;
        public bool CountsTowardHourLimits { get; set; } = true;
    }

    public class RenameCategoryRequest
    {
        public int CategoryId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Color { get; set; }
    }

    public class AssignShiftCategoryRequest
    {
        public int ShiftTypeId { get; set; }
        public int? CategoryId { get; set; }
    }
}
