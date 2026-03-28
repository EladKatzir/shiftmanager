using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Resources;

namespace ShiftManager.Services;

// SECURITY-AUDITED: Store is a global/area-scoped table, access controlled via ManageStores grant.
// IgnoreQueryFilters() used because Store has no tenant query filter (no IBelongsToCompany).
public class StoreService : IStoreService
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<StoreService> _logger;
    private readonly IStringLocalizer<SharedResources> _localizer;

    private const string StoreHoursCacheKeyPrefix = "store-hours-";

    public StoreService(AppDbContext db, IMemoryCache cache, ILogger<StoreService> logger, IStringLocalizer<SharedResources> localizer)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
        _localizer = localizer;
    }

    /// <summary>
    /// Get all stores for a given area, including their hours entries.
    /// </summary>
    // SECURITY-AUDITED: Store is a global/area-scoped table, access controlled via ManageStores grant
    public async Task<List<Store>> GetStoresForAreaAsync(int areaId)
    {
        return await _db.Stores
            .AsNoTracking()
            .Include(s => s.StoreHoursEntries)
            .Where(s => s.AreaId == areaId)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.NameEn)
            .ToListAsync();
    }

    /// <summary>
    /// Get a single store by ID.
    /// </summary>
    // SECURITY-AUDITED: Store is a global/area-scoped table, access controlled via ManageStores grant
    public async Task<Store?> GetStoreByIdAsync(int storeId)
    {
        return await _db.Stores
            .AsNoTracking()
            .Include(s => s.StoreHoursEntries)
            .FirstOrDefaultAsync(s => s.Id == storeId);
    }

    /// <summary>
    /// Create a new store within an area.
    /// </summary>
    public async Task<(bool Success, string Message, Store? Store)> CreateStoreAsync(
        int areaId, string nameEn, string? nameHe, int sortOrder = 0)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(nameEn))
            {
                return (false, _localizer["Store_ErrorNameRequired"].Value, null);
            }

            // Verify area exists
            var areaExists = await _db.Areas.AnyAsync(a => a.Id == areaId);
            if (!areaExists)
            {
                return (false, _localizer["Store_ErrorAreaNotFound"].Value, null);
            }

            var store = new Store
            {
                AreaId = areaId,
                NameEn = nameEn.Trim(),
                NameHe = nameHe?.Trim(),
                SortOrder = sortOrder,
                IsActive = true
            };

            _db.Stores.Add(store);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Store {StoreId} '{StoreName}' created in area {AreaId}",
                store.Id, store.NameEn, areaId);

            return (true, _localizer["Store_SuccessCreated"].Value, store);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error creating store in area {AreaId}", areaId);
            return (false, _localizer["Store_ErrorCreateFailed"].Value, null);
        }
    }

    /// <summary>
    /// Update an existing store.
    /// </summary>
    public async Task<(bool Success, string Message)> UpdateStoreAsync(
        int storeId, string nameEn, string? nameHe, bool isActive, int sortOrder)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(nameEn))
            {
                return (false, _localizer["Store_ErrorNameRequired"].Value);
            }

            var store = await _db.Stores.FirstOrDefaultAsync(s => s.Id == storeId);
            if (store == null)
            {
                return (false, _localizer["Store_ErrorNotFound"].Value);
            }

            store.NameEn = nameEn.Trim();
            store.NameHe = nameHe?.Trim();
            store.IsActive = isActive;
            store.SortOrder = sortOrder;

            await _db.SaveChangesAsync();

            _logger.LogInformation("Store {StoreId} updated: Name='{StoreName}', IsActive={IsActive}",
                storeId, store.NameEn, isActive);

            return (true, _localizer["Store_SuccessUpdated"].Value);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error updating store {StoreId}", storeId);
            return (false, _localizer["Store_ErrorUpdateFailed"].Value);
        }
    }

    /// <summary>
    /// Delete a store and clean up orphaned QuickInfoConfig entries.
    /// </summary>
    public async Task<(bool Success, string Message)> DeleteStoreAsync(int storeId)
    {
        try
        {
            var store = await _db.Stores
                .Include(s => s.StoreHoursEntries)
                .FirstOrDefaultAsync(s => s.Id == storeId);

            if (store == null)
            {
                return (false, _localizer["Store_ErrorNotFound"].Value);
            }

            // Remove orphaned QuickInfoConfig entries (polymorphic FK cleanup)
            var orphanedConfigs = await _db.QuickInfoConfigs
                .Where(q => q.SectionType == QuickInfoSectionType.Store && q.EntityId == storeId)
                .ToListAsync();

            if (orphanedConfigs.Any())
            {
                _db.QuickInfoConfigs.RemoveRange(orphanedConfigs);
            }

            // Remove store hours entries
            if (store.StoreHoursEntries.Any())
            {
                _db.StoreHoursEntries.RemoveRange(store.StoreHoursEntries);
            }

            _db.Stores.Remove(store);
            await _db.SaveChangesAsync();

            // Invalidate cache
            _cache.Remove($"{StoreHoursCacheKeyPrefix}{storeId}");

            _logger.LogInformation("Store {StoreId} '{StoreName}' deleted. Cleaned up {OrphanCount} QuickInfoConfig entries",
                storeId, store.NameEn, orphanedConfigs.Count);

            return (true, _localizer["Store_SuccessDeleted"].Value);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error deleting store {StoreId}", storeId);
            return (false, _localizer["Store_ErrorDeleteFailed"].Value);
        }
    }

    /// <summary>
    /// Get store hours entries, using cache when available.
    /// </summary>
    public async Task<List<StoreHoursEntry>> GetStoreHoursAsync(int storeId)
    {
        string cacheKey = $"{StoreHoursCacheKeyPrefix}{storeId}";

        if (_cache.TryGetValue(cacheKey, out List<StoreHoursEntry>? cached) && cached != null)
        {
            return cached;
        }

        var entries = await _db.StoreHoursEntries
            .AsNoTracking()
            .Where(e => e.StoreId == storeId)
            .OrderBy(e => e.DayOfWeek)
            .ThenBy(e => e.OpenTime)
            .ToListAsync();

        var cacheOptions = new MemoryCacheEntryOptions()
            .SetSlidingExpiration(TimeSpan.FromMinutes(5));

        _cache.Set(cacheKey, entries, cacheOptions);

        return entries;
    }

    /// <summary>
    /// Replace all store hours entries for a store (delete-and-reinsert pattern).
    /// </summary>
    public async Task<(bool Success, string Message)> SetStoreHoursAsync(int storeId, List<StoreHoursEntry> entries)
    {
        try
        {
            var storeExists = await _db.Stores.AnyAsync(s => s.Id == storeId);
            if (!storeExists)
            {
                return (false, _localizer["Store_ErrorNotFound"].Value);
            }

            // Remove existing entries
            var existing = await _db.StoreHoursEntries
                .Where(e => e.StoreId == storeId)
                .ToListAsync();

            _db.StoreHoursEntries.RemoveRange(existing);

            // Add new entries
            foreach (var entry in entries)
            {
                entry.Id = 0; // Ensure new entries get auto-generated IDs
                entry.StoreId = storeId;
                _db.StoreHoursEntries.Add(entry);
            }

            await _db.SaveChangesAsync();

            // Invalidate cache
            _cache.Remove($"{StoreHoursCacheKeyPrefix}{storeId}");

            _logger.LogInformation("Store hours set for store {StoreId}: {EntryCount} entries",
                storeId, entries.Count);

            return (true, _localizer["Store_SuccessHoursUpdated"].Value);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error setting store hours for store {StoreId}", storeId);
            return (false, _localizer["Store_ErrorHoursUpdateFailed"].Value);
        }
    }

    /// <summary>
    /// Compute current open/closed/break status for a single store.
    /// </summary>
    public async Task<StoreStatus?> ComputeStoreStatusAsync(int storeId, DateTime now)
    {
        var store = await _db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.Id == storeId);
        if (store == null) return null;

        var storeName = store.Name ?? "";

        var hours = await GetStoreHoursAsync(storeId);
        return ComputeStatusFromHours(storeId, storeName, hours, now);
    }

    /// <summary>
    /// Batch version — load all store hours in one query, compute status for each.
    /// </summary>
    public async Task<List<StoreStatus>> ComputeAllStoreStatusesAsync(List<int> storeIds, DateTime now)
    {
        if (storeIds == null || !storeIds.Any())
            return new List<StoreStatus>();

        // Load all stores and their hours in one query
        var stores = await _db.Stores
            .AsNoTracking()
            .Include(s => s.StoreHoursEntries)
            .Where(s => storeIds.Contains(s.Id))
            .ToListAsync();

        var results = new List<StoreStatus>();
        foreach (var store in stores)
        {
            var status = ComputeStatusFromHours(store.Id, store.Name, store.StoreHoursEntries, now);
            results.Add(status);
        }

        return results;
    }

    /// <summary>
    /// Core status computation logic shared by single and batch methods.
    /// </summary>
    private StoreStatus ComputeStatusFromHours(int storeId, string storeName, IEnumerable<StoreHoursEntry> allHours, DateTime now)
    {
        var todayDow = (int)now.DayOfWeek;
        var currentTime = TimeOnly.FromDateTime(now);

        var todayEntries = allHours
            .Where(e => e.DayOfWeek == todayDow && e.IsActive)
            .OrderBy(e => e.OpenTime)
            .ToList();

        // No hours set for today
        if (!todayEntries.Any())
        {
            return new StoreStatus
            {
                StoreId = storeId,
                StoreName = storeName,
                Status = StoreStatusType.NoHoursSet,
                Message = _localizer["Widget_NoHoursSet"].Value
            };
        }

        // Before first window opens
        if (currentTime < todayEntries[0].OpenTime)
        {
            return new StoreStatus
            {
                StoreId = storeId,
                StoreName = storeName,
                Status = StoreStatusType.Closed,
                Message = _localizer["Widget_StoreClosedOpensAt", todayEntries[0].OpenTime.ToString("HH:mm")].Value,
                NextChange = todayEntries[0].OpenTime
            };
        }

        // Check each window
        for (int i = 0; i < todayEntries.Count; i++)
        {
            var window = todayEntries[i];

            // Currently within this window
            if (currentTime >= window.OpenTime && currentTime < window.CloseTime)
            {
                return new StoreStatus
                {
                    StoreId = storeId,
                    StoreName = storeName,
                    Status = StoreStatusType.Open,
                    Message = _localizer["Widget_StoreOpenUntil", window.CloseTime.ToString("HH:mm")].Value,
                    NextChange = window.CloseTime
                };
            }

            // Between this window's close and the next window's open
            if (i < todayEntries.Count - 1)
            {
                var nextWindow = todayEntries[i + 1];
                if (currentTime >= window.CloseTime && currentTime < nextWindow.OpenTime)
                {
                    return new StoreStatus
                    {
                        StoreId = storeId,
                        StoreName = storeName,
                        Status = StoreStatusType.Break,
                        Message = _localizer["Widget_StoreBreakReopens", nextWindow.OpenTime.ToString("HH:mm")].Value,
                        NextChange = nextWindow.OpenTime
                    };
                }
            }
        }

        // After all windows — scan forward up to 7 days for next open time
        var nextOpenTime = FindNextOpenTime(allHours, todayDow);

        var result = new StoreStatus
        {
            StoreId = storeId,
            StoreName = storeName,
            Status = StoreStatusType.Closed,
            Message = _localizer["Widget_StoreClosed"].Value
        };

        if (nextOpenTime.HasValue)
        {
            result.Message = _localizer["Widget_StoreClosedOpensDay", nextOpenTime.Value.dayName, nextOpenTime.Value.time.ToString("HH:mm")].Value;
            result.NextChange = nextOpenTime.Value.time;
        }

        return result;
    }

    /// <summary>
    /// Scan forward up to 7 days from the given day-of-week to find the next open time.
    /// </summary>
    private static (string dayName, TimeOnly time)? FindNextOpenTime(IEnumerable<StoreHoursEntry> allHours, int todayDow)
    {
        var dayNames = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames;

        for (int offset = 1; offset <= 7; offset++)
        {
            var checkDow = (todayDow + offset) % 7;
            var dayEntries = allHours
                .Where(e => e.DayOfWeek == checkDow && e.IsActive)
                .OrderBy(e => e.OpenTime)
                .ToList();

            if (dayEntries.Any())
            {
                return (dayNames[checkDow], dayEntries[0].OpenTime);
            }
        }

        return null;
    }
}
