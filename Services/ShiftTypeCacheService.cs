using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Service interface for caching shift types per company
/// </summary>
public interface IShiftTypeCacheService
{
    Task<List<ShiftType>> GetShiftTypesAsync(int companyId);
    Task<List<ShiftType>> GetShiftTypesForMoleculeAsync(int moleculeId, int jobTypeId);
    void InvalidateCache(int companyId);
    void InvalidateMoleculeCache(int moleculeId, int jobTypeId);
}

/// <summary>
/// Service for caching frequently-accessed shift types to reduce database queries
/// Caches shift types per company for 10 minutes
/// </summary>
public class ShiftTypeCacheService : IShiftTypeCacheService
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ShiftTypeCacheService> _logger;

    private const int CacheDurationMinutes = 10;

    public ShiftTypeCacheService(
        AppDbContext db,
        IMemoryCache cache,
        ILogger<ShiftTypeCacheService> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Get shift types for a company, using cache when available
    /// </summary>
    public async Task<List<ShiftType>> GetShiftTypesAsync(int companyId)
    {
        string cacheKey = $"ShiftTypes_{companyId}";

        if (_cache.TryGetValue(cacheKey, out List<ShiftType>? shiftTypes) && shiftTypes != null)
        {
            _logger.LogDebug("ShiftTypes cache hit for company {CompanyId}", companyId);
            return shiftTypes;
        }

        _logger.LogDebug("ShiftTypes cache miss for company {CompanyId}, loading from database", companyId);

        // Load from database with company filter
        shiftTypes = await _db.ShiftTypes
            .AsNoTracking()
            .Where(st => st.CompanyId == companyId)
            .OrderBy(st => st.Key)
            .ToListAsync();

        // Cache for 10 minutes
        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(CacheDurationMinutes))
            .SetSize(1); // Estimate size for cache eviction policies

        _cache.Set(cacheKey, shiftTypes, cacheOptions);

        _logger.LogInformation("Cached {Count} shift types for company {CompanyId}", shiftTypes.Count, companyId);

        return shiftTypes;
    }

    /// <summary>
    /// Get shift types for a molecule+jobtype combination, using cache when available.
    /// Uses IgnoreQueryFilters to load across company boundaries within the molecule.
    /// </summary>
    public async Task<List<ShiftType>> GetShiftTypesForMoleculeAsync(int moleculeId, int jobTypeId)
    {
        string cacheKey = $"ShiftTypes_Molecule_{moleculeId}_{jobTypeId}";

        if (_cache.TryGetValue(cacheKey, out List<ShiftType>? shiftTypes) && shiftTypes != null)
        {
            _logger.LogDebug("ShiftTypes molecule cache hit for molecule {MoleculeId} jobType {JobTypeId}", moleculeId, jobTypeId);
            return shiftTypes;
        }

        _logger.LogDebug("ShiftTypes molecule cache miss for molecule {MoleculeId} jobType {JobTypeId}, loading from database", moleculeId, jobTypeId);

        // SECURITY-AUDITED: SAFE — IgnoreQueryFilters needed for cross-company ShiftType loading;
        // re-scoped by explicit moleculeId and jobTypeId parameters
        shiftTypes = await _db.ShiftTypes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(st => st.MoleculeId == moleculeId && st.JobTypeId == jobTypeId)
            .OrderBy(st => st.Start)
            .ToListAsync();

        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(CacheDurationMinutes))
            .SetSize(1);

        _cache.Set(cacheKey, shiftTypes, cacheOptions);

        _logger.LogInformation("Cached {Count} shift types for molecule {MoleculeId} jobType {JobTypeId}",
            shiftTypes.Count, moleculeId, jobTypeId);

        return shiftTypes;
    }

    /// <summary>
    /// Invalidate the shift types cache for a company
    /// Call this when shift types are created, updated, or deleted
    /// </summary>
    public void InvalidateCache(int companyId)
    {
        string cacheKey = $"ShiftTypes_{companyId}";
        _cache.Remove(cacheKey);
        _logger.LogInformation("Invalidated ShiftTypes cache for company {CompanyId}", companyId);
    }

    /// <summary>
    /// Invalidate the molecule-scoped shift types cache.
    /// Call this when shift types are created, updated, or deleted for a molecule+jobtype combination.
    /// </summary>
    public void InvalidateMoleculeCache(int moleculeId, int jobTypeId)
    {
        string cacheKey = $"ShiftTypes_Molecule_{moleculeId}_{jobTypeId}";
        _cache.Remove(cacheKey);
        _logger.LogInformation("Invalidated ShiftTypes molecule cache for molecule {MoleculeId} jobType {JobTypeId}", moleculeId, jobTypeId);
    }
}
