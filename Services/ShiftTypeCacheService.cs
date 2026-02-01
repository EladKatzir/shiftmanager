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
    void InvalidateCache(int companyId);
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
    /// Invalidate the shift types cache for a company
    /// Call this when shift types are created, updated, or deleted
    /// </summary>
    public void InvalidateCache(int companyId)
    {
        string cacheKey = $"ShiftTypes_{companyId}";
        _cache.Remove(cacheKey);
        _logger.LogInformation("Invalidated ShiftTypes cache for company {CompanyId}", companyId);
    }
}
