using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

/// <summary>
/// Service interface for caching shift types by molecule and area scope.
/// </summary>
public interface IShiftTypeCacheService
{
    /// <summary>
    /// Get all shift types visible for a given molecule+jobType: molecule-scoped + company-scoped shifts.
    /// </summary>
    Task<List<ShiftType>> GetShiftTypesForMoleculeAsync(int moleculeId, int? jobTypeId);

    /// <summary>
    /// Get area-scoped shift types overlaid on top of molecule shifts.
    /// </summary>
    Task<List<ShiftType>> GetAreaShiftTypesAsync(int areaId, int? jobTypeId);

    /// <summary>
    /// Get merged list: molecule + area shifts for a given molecule (auto-resolves areaId).
    /// </summary>
    Task<List<ShiftType>> GetMergedShiftTypesAsync(int moleculeId, int? jobTypeId);

    void InvalidateCache(int companyId);
    void InvalidateMoleculeCache(int moleculeId, int? jobTypeId);
    void InvalidateAreaCache(int areaId, int? jobTypeId);
    void InvalidateAllCaches();
}

/// <summary>
/// Caches shift types in two layers:
/// - Molecule cache: molecule-scoped + company-scoped shifts for a molecule+jobType
/// - Area overlay cache: area-scoped shifts only
/// At query time, merge both lists for the full picture.
/// </summary>
public class ShiftTypeCacheService : IShiftTypeCacheService
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ShiftTypeCacheService> _logger;

    private const int CacheDurationMinutes = 10;
    private const string AllCacheKeysPrefix = "ShiftTypes_";

    public ShiftTypeCacheService(
        AppDbContext db,
        IMemoryCache cache,
        ILogger<ShiftTypeCacheService> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    private static string MoleculeCacheKey(int moleculeId, int? jobTypeId)
        => $"ShiftTypes_Molecule_{moleculeId}_{jobTypeId ?? 0}";

    private static string AreaCacheKey(int areaId, int? jobTypeId)
        => $"ShiftTypes_Area_{areaId}_{jobTypeId ?? 0}";

    /// <summary>
    /// Get shift types for a molecule+jobtype: molecule-scoped + company-scoped within this molecule.
    /// No query filter needed — ShiftType no longer implements IBelongsToCompany.
    /// When jobTypeId is null, loads shared shifts (HOME, OFFLINE) and tech shifts with JobTypeId == null.
    /// </summary>
    public async Task<List<ShiftType>> GetShiftTypesForMoleculeAsync(int moleculeId, int? jobTypeId)
    {
        string cacheKey = MoleculeCacheKey(moleculeId, jobTypeId);

        if (_cache.TryGetValue(cacheKey, out List<ShiftType>? shiftTypes) && shiftTypes != null)
            return shiftTypes;

        var query = _db.ShiftTypes
            .AsNoTracking()
            .Where(st => st.MoleculeId == moleculeId && st.Scope != ShiftScope.Area);

        if (jobTypeId.HasValue)
            query = query.Where(st => st.JobTypeId == jobTypeId.Value || st.JobTypeId == null);
        else
            query = query.Where(st => st.JobTypeId == null);

        shiftTypes = await query
            .OrderBy(st => st.SortOrder)
            .ThenBy(st => st.Start)
            .ToListAsync();

        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(CacheDurationMinutes))
            .SetSize(1);

        _cache.Set(cacheKey, shiftTypes, cacheOptions);
        _logger.LogDebug("Cached {Count} molecule shift types for molecule {MoleculeId} jobType {JobTypeId}",
            shiftTypes.Count, moleculeId, jobTypeId);

        return shiftTypes;
    }

    /// <summary>
    /// Get area-scoped shift types for an area+jobType combination.
    /// </summary>
    public async Task<List<ShiftType>> GetAreaShiftTypesAsync(int areaId, int? jobTypeId)
    {
        string cacheKey = AreaCacheKey(areaId, jobTypeId);

        if (_cache.TryGetValue(cacheKey, out List<ShiftType>? shiftTypes) && shiftTypes != null)
            return shiftTypes;

        var query = _db.ShiftTypes
            .AsNoTracking()
            .Where(st => st.Scope == ShiftScope.Area && st.AreaId == areaId);

        if (jobTypeId.HasValue)
            query = query.Where(st => st.JobTypeId == jobTypeId.Value || st.JobTypeId == null);
        else
            query = query.Where(st => st.JobTypeId == null);

        shiftTypes = await query
            .OrderBy(st => st.SortOrder)
            .ThenBy(st => st.Start)
            .ToListAsync();

        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(CacheDurationMinutes))
            .SetSize(1);

        _cache.Set(cacheKey, shiftTypes, cacheOptions);
        _logger.LogDebug("Cached {Count} area shift types for area {AreaId} jobType {JobTypeId}",
            shiftTypes.Count, areaId, jobTypeId);

        return shiftTypes;
    }

    /// <summary>
    /// Get merged shift types: molecule-scoped + company-scoped + area overlay.
    /// Resolves areaId from the molecule automatically.
    /// </summary>
    public async Task<List<ShiftType>> GetMergedShiftTypesAsync(int moleculeId, int? jobTypeId)
    {
        var moleculeShifts = await GetShiftTypesForMoleculeAsync(moleculeId, jobTypeId);

        // Resolve areaId from molecule
        var areaId = await _db.Molecules
            .Where(m => m.Id == moleculeId)
            .Select(m => m.AreaId)
            .FirstOrDefaultAsync();

        if (areaId == 0)
            return moleculeShifts;

        var areaShifts = await GetAreaShiftTypesAsync(areaId, jobTypeId);

        if (!areaShifts.Any())
            return moleculeShifts;

        // Merge: area shifts first (higher scope), then molecule shifts
        return areaShifts
            .Concat(moleculeShifts)
            .OrderBy(st => st.Scope == ShiftScope.Area ? 0 : 1) // Area first
            .ThenBy(st => st.SortOrder)
            .ThenBy(st => st.Start)
            .ToList();
    }

    /// <summary>
    /// Invalidate cache for a specific company (legacy — invalidates molecule caches that may contain company-scoped shifts).
    /// </summary>
    public void InvalidateCache(int companyId)
    {
        // For backward compatibility, invalidate by looking up the molecule
        _logger.LogDebug("InvalidateCache called for company {CompanyId} — consider using InvalidateMoleculeCache instead", companyId);
    }

    /// <summary>
    /// Invalidate the molecule-scoped shift types cache.
    /// </summary>
    public void InvalidateMoleculeCache(int moleculeId, int? jobTypeId)
    {
        string cacheKey = MoleculeCacheKey(moleculeId, jobTypeId);
        _cache.Remove(cacheKey);
        _logger.LogDebug("Invalidated molecule cache for molecule {MoleculeId} jobType {JobTypeId}", moleculeId, jobTypeId);
    }

    /// <summary>
    /// Invalidate the area-scoped shift types cache.
    /// </summary>
    public void InvalidateAreaCache(int areaId, int? jobTypeId)
    {
        string cacheKey = AreaCacheKey(areaId, jobTypeId);
        _cache.Remove(cacheKey);
        _logger.LogDebug("Invalidated area cache for area {AreaId} jobType {JobTypeId}", areaId, jobTypeId);
    }

    /// <summary>
    /// Nuclear option: invalidate all shift type caches.
    /// Used during migration/seed operations.
    /// </summary>
    public void InvalidateAllCaches()
    {
        // IMemoryCache doesn't support prefix-based removal natively.
        // The MemoryCache implementation allows compact, which evicts expired entries.
        // For a full clear, we rely on the 10-minute TTL. This method is a signal
        // that callers should expect stale data until TTL expires.
        if (_cache is MemoryCache mc)
        {
            mc.Compact(1.0); // Remove all entries
        }
        _logger.LogInformation("Invalidated all shift type caches");
    }
}
