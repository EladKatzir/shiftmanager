using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Service interface for caching app configuration per company
/// </summary>
public interface IAppConfigCacheService
{
    Task<AppConfig?> GetConfigAsync(int companyId, string key);
    Task<Dictionary<string, string>> GetAllConfigsAsync(int companyId);
    void InvalidateCache(int companyId);
}

/// <summary>
/// Service for caching company configuration settings to reduce database queries
/// Caches config settings per company for 5 minutes
/// </summary>
public class AppConfigCacheService : IAppConfigCacheService
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AppConfigCacheService> _logger;

    private const int CacheDurationMinutes = 5;

    public AppConfigCacheService(
        AppDbContext db,
        IMemoryCache cache,
        ILogger<AppConfigCacheService> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Get a specific config value for a company, using cache when available
    /// </summary>
    public async Task<AppConfig?> GetConfigAsync(int companyId, string key)
    {
        var allConfigs = await GetAllConfigsAsync(companyId);

        if (allConfigs.TryGetValue(key, out var value))
        {
            return new AppConfig
            {
                CompanyId = companyId,
                Key = key,
                Value = value
            };
        }

        return null;
    }

    /// <summary>
    /// Get all config settings for a company as a dictionary, using cache when available
    /// </summary>
    public async Task<Dictionary<string, string>> GetAllConfigsAsync(int companyId)
    {
        string cacheKey = $"AppConfigs_{companyId}";

        if (_cache.TryGetValue(cacheKey, out Dictionary<string, string>? configs) && configs != null)
        {
            _logger.LogDebug("AppConfigs cache hit for company {CompanyId}", companyId);
            return configs;
        }

        _logger.LogDebug("AppConfigs cache miss for company {CompanyId}, loading from database", companyId);

        // Load from database with company filter
        var configList = await _db.Configs
            .AsNoTracking()
            .Where(c => c.CompanyId == companyId)
            .ToListAsync();

        // Convert to dictionary for fast lookup
        configs = configList.GroupBy(c => c.Key).ToDictionary(g => g.Key, g => g.Last().Value);

        // Cache for 5 minutes
        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(CacheDurationMinutes))
            .SetSize(1); // Estimate size for cache eviction policies

        _cache.Set(cacheKey, configs, cacheOptions);

        _logger.LogInformation("Cached {Count} config settings for company {CompanyId}", configs.Count, companyId);

        return configs;
    }

    /// <summary>
    /// Invalidate the config cache for a company
    /// Call this when config settings are created, updated, or deleted
    /// </summary>
    public void InvalidateCache(int companyId)
    {
        string cacheKey = $"AppConfigs_{companyId}";
        _cache.Remove(cacheKey);
        _logger.LogInformation("Invalidated AppConfigs cache for company {CompanyId}", companyId);
    }
}
