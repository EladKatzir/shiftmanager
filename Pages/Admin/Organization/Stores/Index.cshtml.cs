using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Data.SeedData;
using ShiftManager.Models;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Pages.Admin.Organization.Stores;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:ManageStores policy;
// Store is a global/area-scoped table (no IBelongsToCompany), access controlled via ManageStores grant
[Authorize(Policy = "Grant:ManageStores")]
public class IndexModel : LocalizedPageModel
{
    private readonly IStoreService _storeService;
    private readonly AppDbContext _db;
    private readonly ILogger<IndexModel> _logger;
    private readonly IFeatureFlagService _featureFlagService;
    private readonly IGrantService _grantService;
    private readonly ICompanyContext _companyContext;

    public IndexModel(
        IStringLocalizer<SharedResources> localizer,
        IStoreService storeService,
        AppDbContext db,
        ILogger<IndexModel> logger,
        IFeatureFlagService featureFlagService,
        IGrantService grantService,
        ICompanyContext companyContext) : base(localizer)
    {
        _storeService = storeService;
        _db = db;
        _logger = logger;
        _featureFlagService = featureFlagService;
        _grantService = grantService;
        _companyContext = companyContext;
    }

    // View Models
    public record StoreViewModel(int Id, string NameEn, string? NameHe, string AreaName, int AreaId, bool IsActive, int SortOrder, List<HoursGroupViewModel> HoursByDay);
    public record HoursGroupViewModel(int DayOfWeek, string DayName, List<HoursEntryViewModel> Entries);
    public record HoursEntryViewModel(int Id, string OpenTime, string CloseTime, bool IsActive);
    public record AreaOption(int Id, string Name);

    // Data
    public List<StoreViewModel> Stores { get; set; } = new();
    public List<AreaOption> AvailableAreas { get; set; } = new();

    // Create form
    [BindProperty] public string NewStoreName { get; set; } = "";
    [BindProperty] public string? NewStoreNameHe { get; set; }
    [BindProperty] public int NewStoreAreaId { get; set; }
    [BindProperty] public int NewStoreSortOrder { get; set; }

    private static readonly string[] DayNames = { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };

    public async Task<IActionResult> OnGetAsync()
    {
        // Feature flag gate
        if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.StoreHoursEnabled))
            return RedirectToPage("/Admin/Organization/Index");


        await LoadDataAsync();
        return Page();
    }

    private async Task LoadDataAsync()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId)) return;

        var accessibleAreaIds = await GetAccessibleAreaIdsAsync(currentUserId);
        if (!accessibleAreaIds.Any()) return;

        // SECURITY-AUDITED: SAFE — filtered to only areas the user has grant access to
        AvailableAreas = await _db.Areas
            .IgnoreQueryFilters()
            .Where(a => accessibleAreaIds.Contains(a.Id) && a.IsActive)
            .OrderBy(a => a.DisplayName)
            .Select(a => new AreaOption(a.Id, a.DisplayName))
            .ToListAsync();

        // Load stores for all accessible areas
        var allStores = new List<Store>();
        foreach (var areaId in accessibleAreaIds)
        {
            var stores = await _storeService.GetStoresForAreaAsync(areaId);
            allStores.AddRange(stores);
        }

        // Map to view models — include area name from loaded areas
        var areaNames = AvailableAreas.ToDictionary(a => a.Id, a => a.Name);

        Stores = allStores
            .OrderBy(s => s.AreaId)
            .ThenBy(s => s.SortOrder)
            .ThenBy(s => s.NameEn)
            .Select(s => new StoreViewModel(
                s.Id,
                s.NameEn,
                s.NameHe,
                areaNames.GetValueOrDefault(s.AreaId, ""),
                s.AreaId,
                s.IsActive,
                s.SortOrder,
                BuildHoursGroups(s.StoreHoursEntries)
            ))
            .ToList();
    }

    private static List<HoursGroupViewModel> BuildHoursGroups(IEnumerable<StoreHoursEntry> entries)
    {
        var groups = new List<HoursGroupViewModel>();
        for (int day = 0; day < 7; day++)
        {
            var dayEntries = entries
                .Where(e => e.DayOfWeek == day)
                .OrderBy(e => e.OpenTime)
                .Select(e => new HoursEntryViewModel(
                    e.Id,
                    e.OpenTime.ToString("HH:mm"),
                    e.CloseTime.ToString("HH:mm"),
                    e.IsActive))
                .ToList();

            groups.Add(new HoursGroupViewModel(day, DayNames[day], dayEntries));
        }
        return groups;
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        // Feature flag gate
        if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.StoreHoursEnabled))
            return RedirectToPage("/Admin/Organization/Index");

        if (string.IsNullOrWhiteSpace(NewStoreName))
        {
            TempData["ErrorMessage"] = _localizer["Error_StoreNameRequired"].Value;
            return RedirectToPage();
        }

        if (NewStoreName.Trim().Length > 100)
        {
            TempData["ErrorMessage"] = _localizer["Error_StoreNameRequired"].Value;
            return RedirectToPage();
        }

        if (NewStoreAreaId <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_StoreNotFound"].Value;
            return RedirectToPage();
        }

        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
        {
            TempData["ErrorMessage"] = _localizer["Error_StoreNotFound"].Value;
            return RedirectToPage();
        }

        // IDOR prevention: verify user has access to the target area
        var accessibleAreaIds = await GetAccessibleAreaIdsAsync(currentUserId);
        if (!accessibleAreaIds.Contains(NewStoreAreaId))
        {
            TempData["ErrorMessage"] = _localizer["Error_StoreNotFound"].Value;
            return RedirectToPage();
        }

        var (success, message, store) = await _storeService.CreateStoreAsync(
            NewStoreAreaId,
            NewStoreName.Trim(),
            NewStoreNameHe?.Trim(),
            NewStoreSortOrder);

        if (success)
        {
            _logger.LogInformation("Created Store {StoreId}: {StoreName} in Area {AreaId} by User {UserId}",
                store!.Id, store.NameEn, NewStoreAreaId, currentUserId);
            TempData["SuccessMessage"] = _localizer["Success_StoreCreated"].Value;
        }
        else
        {
            TempData["ErrorMessage"] = message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync(int storeId, string nameEn, string? nameHe, bool isActive, int sortOrder)
    {
        // Feature flag gate
        if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.StoreHoursEnabled))
            return RedirectToPage("/Admin/Organization/Index");

        if (storeId <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_StoreNotFound"].Value;
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(nameEn))
        {
            TempData["ErrorMessage"] = _localizer["Error_StoreNameRequired"].Value;
            return RedirectToPage();
        }

        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
        {
            TempData["ErrorMessage"] = _localizer["Error_StoreNotFound"].Value;
            return RedirectToPage();
        }

        // IDOR prevention: verify store belongs to an area the user can access
        var store = await _storeService.GetStoreByIdAsync(storeId);
        var accessibleAreaIds = await GetAccessibleAreaIdsAsync(currentUserId);
        if (store == null || !accessibleAreaIds.Contains(store.AreaId))
        {
            TempData["ErrorMessage"] = _localizer["Error_StoreNotFound"].Value;
            return RedirectToPage();
        }

        var (success, message) = await _storeService.UpdateStoreAsync(
            storeId, nameEn.Trim(), nameHe?.Trim(), isActive, sortOrder);

        if (success)
        {
            _logger.LogInformation("Updated Store {StoreId}: {StoreName} by User {UserId}",
                storeId, nameEn.Trim(), currentUserId);
            TempData["SuccessMessage"] = _localizer["Success_StoreUpdated"].Value;
        }
        else
        {
            TempData["ErrorMessage"] = message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int storeId)
    {
        // Feature flag gate
        if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.StoreHoursEnabled))
            return RedirectToPage("/Admin/Organization/Index");

        if (storeId <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_StoreNotFound"].Value;
            return RedirectToPage();
        }

        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
        {
            TempData["ErrorMessage"] = _localizer["Error_StoreNotFound"].Value;
            return RedirectToPage();
        }

        // IDOR prevention: verify store belongs to an area the user can access
        var store = await _storeService.GetStoreByIdAsync(storeId);
        var accessibleAreaIds = await GetAccessibleAreaIdsAsync(currentUserId);
        if (store == null || !accessibleAreaIds.Contains(store.AreaId))
        {
            TempData["ErrorMessage"] = _localizer["Error_StoreNotFound"].Value;
            return RedirectToPage();
        }

        var (success, message) = await _storeService.DeleteStoreAsync(storeId);

        if (success)
        {
            _logger.LogInformation("Deleted Store {StoreId} by User {UserId}", storeId, currentUserId);
            TempData["SuccessMessage"] = _localizer["Success_StoreDeleted"].Value;
        }
        else
        {
            TempData["ErrorMessage"] = message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSetHoursAsync(int storeId, string hoursJson)
    {
        // Feature flag gate
        if (!await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.StoreHoursEnabled))
            return RedirectToPage("/Admin/Organization/Index");

        if (storeId <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_StoreNotFound"].Value;
            return RedirectToPage();
        }

        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
        {
            TempData["ErrorMessage"] = _localizer["Error_StoreNotFound"].Value;
            return RedirectToPage();
        }

        // IDOR prevention: verify store belongs to an area the user can access
        var store = await _storeService.GetStoreByIdAsync(storeId);
        var accessibleAreaIds = await GetAccessibleAreaIdsAsync(currentUserId);
        if (store == null || !accessibleAreaIds.Contains(store.AreaId))
        {
            TempData["ErrorMessage"] = _localizer["Error_StoreNotFound"].Value;
            return RedirectToPage();
        }

        // Parse the hours JSON
        List<StoreHoursEntry> entries;
        try
        {
            var jsonEntries = JsonSerializer.Deserialize<List<HoursJsonEntry>>(hoursJson ?? "[]",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            entries = (jsonEntries ?? new List<HoursJsonEntry>())
                .Select(e => new StoreHoursEntry
                {
                    StoreId = storeId,
                    DayOfWeek = e.DayOfWeek,
                    OpenTime = TimeOnly.Parse(e.OpenTime),
                    CloseTime = TimeOnly.Parse(e.CloseTime),
                    IsActive = e.IsActive
                })
                .ToList();
        }
        catch (Exception ex) when (ex is JsonException || ex is FormatException)
        {
            _logger.LogWarning(ex, "Invalid hours JSON for Store {StoreId}", storeId);
            TempData["ErrorMessage"] = _localizer["Error_StoreNotFound"].Value;
            return RedirectToPage();
        }

        var (success, message) = await _storeService.SetStoreHoursAsync(storeId, entries);

        if (success)
        {
            _logger.LogInformation("Set hours for Store {StoreId}: {EntryCount} entries by User {UserId}",
                storeId, entries.Count, currentUserId);
            TempData["SuccessMessage"] = _localizer["Success_StoreUpdated"].Value;
        }
        else
        {
            TempData["ErrorMessage"] = message;
        }

        return RedirectToPage();
    }

    /// <summary>
    /// Gets the set of area IDs the current user can access for the ManageStores grant.
    /// Derives areas from accessible molecules (molecules have AreaId).
    /// Also includes the user's own area as fallback.
    /// </summary>
    private async Task<HashSet<int>> GetAccessibleAreaIdsAsync(int currentUserId)
    {
        // Get accessible molecules, then derive distinct AreaIds
        var moleculeIds = await _grantService.GetAccessibleMoleculeIdsForGrantAsync(currentUserId, "ManageStores");

        // SECURITY-AUDITED: SAFE — filtered to only molecules the user has grant access to;
        // we only extract AreaId, no data leakage
        var areaIds = new HashSet<int>(
            await _db.Molecules
                .IgnoreQueryFilters()
                .Where(m => moleculeIds.Contains(m.Id))
                .Select(m => m.AreaId)
                .Distinct()
                .ToListAsync());

        // Fallback: add user's own area from their company context
        var companyId = _companyContext.CompanyId;
        if (companyId.HasValue)
        {
            var userCompany = await _db.Companies
                .Include(c => c.Molecule)
                .FirstOrDefaultAsync(c => c.Id == companyId.Value);
            if (userCompany?.Molecule?.AreaId is int userAreaId)
                areaIds.Add(userAreaId);
        }

        return areaIds;
    }

    /// <summary>
    /// JSON deserialization model for hours entries posted from the client.
    /// </summary>
    private record HoursJsonEntry(int DayOfWeek, string OpenTime, string CloseTime, bool IsActive);
}
