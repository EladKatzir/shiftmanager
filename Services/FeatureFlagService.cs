using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Service for managing feature flags with memory caching.
/// Cache entries expire after 1 minute to ensure flag changes take effect quickly.
/// </summary>
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — feature flags are global configuration;
// queries scoped by explicit companyId parameter; no sensitive user data exposed
public class FeatureFlagService : IFeatureFlagService
{
    private readonly AppDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ILogger<FeatureFlagService> _logger;

    // Cache settings
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan WarmCacheExpiration = TimeSpan.FromMinutes(5);
    private const string CacheKeyPrefix = "FeatureFlag_";
    private const string AllFlagsCacheKey = "FeatureFlags_All";
    private const string WarmCacheKey = "FeatureFlags_Warm";

    public FeatureFlagService(
        AppDbContext context,
        IMemoryCache cache,
        ILogger<FeatureFlagService> logger)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<bool> IsEnabledAsync(string flagName, int? userId = null, int? companyId = null)
    {
        var cacheKey = BuildCacheKey(flagName, userId, companyId);

        if (_cache.TryGetValue(cacheKey, out bool cachedValue))
        {
            return cachedValue;
        }

        // Resolution priority: user-specific > company-specific > global
        bool isEnabled = await ResolveFlag(flagName, userId, companyId);

        // Cache the result
        _cache.Set(cacheKey, isEnabled, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheExpiration
        });

        return isEnabled;
    }

    /// <summary>
    /// Resolves the flag value with priority: user-specific > company-specific > global.
    /// </summary>
    private async Task<bool> ResolveFlag(string flagName, int? userId, int? companyId)
    {
        // Query filters are ignored to access flags across all scopes
        // SECURITY-AUDITED: SAFE — flags have their own 3-tier scope resolution (user > company > global)
        var flags = await _context.FeatureFlags
            .IgnoreQueryFilters()
            .Where(f => f.Name == flagName)
            .ToListAsync();

        if (!flags.Any())
        {
            _logger.LogDebug("Feature flag {FlagName} not found, returning false", flagName);
            return false;
        }

        // Priority 1: User-specific flag (most specific)
        if (userId.HasValue && companyId.HasValue)
        {
            var userFlag = flags.FirstOrDefault(f => f.UserId == userId && f.CompanyId == companyId);
            if (userFlag != null)
            {
                _logger.LogDebug("Feature flag {FlagName} resolved from user-specific setting: {IsEnabled}",
                    flagName, userFlag.IsEnabled);
                return userFlag.IsEnabled;
            }
        }

        // Priority 2: Company-specific flag
        if (companyId.HasValue)
        {
            var companyFlag = flags.FirstOrDefault(f => f.CompanyId == companyId && f.UserId == null);
            if (companyFlag != null)
            {
                _logger.LogDebug("Feature flag {FlagName} resolved from company-specific setting: {IsEnabled}",
                    flagName, companyFlag.IsEnabled);
                return companyFlag.IsEnabled;
            }
        }

        // Priority 3: Global flag (least specific)
        var globalFlag = flags.FirstOrDefault(f => f.CompanyId == null && f.UserId == null);
        if (globalFlag != null)
        {
            _logger.LogDebug("Feature flag {FlagName} resolved from global setting: {IsEnabled}",
                flagName, globalFlag.IsEnabled);
            return globalFlag.IsEnabled;
        }

        _logger.LogDebug("Feature flag {FlagName} has no applicable setting, returning false", flagName);
        return false;
    }

    /// <inheritdoc/>
    public async Task SetFlagAsync(string flagName, bool isEnabled, int? companyId = null, int? userId = null, string? description = null)
    {
        // Find existing flag with exact scope match
        // SECURITY-AUDITED: SAFE — scoped by exact flagName + companyId + userId match
        var flag = await _context.FeatureFlags
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(f =>
                f.Name == flagName &&
                f.CompanyId == companyId &&
                f.UserId == userId);

        if (flag != null)
        {
            // Update existing flag
            flag.IsEnabled = isEnabled;
            flag.UpdatedAt = DateTime.UtcNow;
            if (description != null)
            {
                flag.Description = description;
            }

            _logger.LogInformation("Updated feature flag {FlagName} (Company: {CompanyId}, User: {UserId}) to {IsEnabled}",
                flagName, companyId, userId, isEnabled);
        }
        else
        {
            // Create new flag
            flag = new FeatureFlag
            {
                Name = flagName,
                IsEnabled = isEnabled,
                Description = description,
                CompanyId = companyId,
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.FeatureFlags.Add(flag);

            _logger.LogInformation("Created feature flag {FlagName} (Company: {CompanyId}, User: {UserId}) with value {IsEnabled}",
                flagName, companyId, userId, isEnabled);
        }

        await _context.SaveChangesAsync();

        // Invalidate cache for this flag
        InvalidateCache(flagName, companyId, userId);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<FeatureFlag>> GetAllFlagsAsync(int? companyId = null)
    {
        // SECURITY-AUDITED: SAFE — re-filtered by companyId when provided; admin-only endpoint
        var query = _context.FeatureFlags
            .IgnoreQueryFilters()
            .AsQueryable();

        if (companyId.HasValue)
        {
            // Get global flags and company-specific flags
            query = query.Where(f => f.CompanyId == null || f.CompanyId == companyId);
        }

        return await query
            .OrderBy(f => f.Name)
            .ThenBy(f => f.CompanyId)
            .ThenBy(f => f.UserId)
            .ToListAsync();
    }

    /// <inheritdoc/>
    public async Task<FeatureFlag?> GetFlagAsync(string flagName, int? companyId = null, int? userId = null)
    {
        // SECURITY-AUDITED: SAFE — scoped by exact flagName + companyId + userId match
        return await _context.FeatureFlags
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(f =>
                f.Name == flagName &&
                f.CompanyId == companyId &&
                f.UserId == userId);
    }

    /// <inheritdoc/>
    public async Task DeleteFlagAsync(int flagId)
    {
        // SECURITY-AUDITED: SAFE — scoped by specific flagId; admin-only delete operation
        var flag = await _context.FeatureFlags
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(f => f.Id == flagId);

        if (flag != null)
        {
            var flagName = flag.Name;
            _context.FeatureFlags.Remove(flag);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Deleted feature flag {FlagName} (Id: {FlagId})", flagName, flagId);

            // Invalidate cache for this flag
            InvalidateCache(flagName, flag.CompanyId, flag.UserId);
        }
    }

    /// <inheritdoc/>
    public void InvalidateCache(string flagName, int? companyId = null, int? userId = null)
    {
        var cacheKey = BuildCacheKey(flagName, userId, companyId);
        _cache.Remove(cacheKey);

        // Also invalidate the warm cache so it gets refreshed on next WarmCacheAsync or IsEnabled
        _cache.Remove(WarmCacheKey);

        _logger.LogDebug("Cache invalidated for feature flag {FlagName} (key: {CacheKey})", flagName, cacheKey);
    }

    /// <inheritdoc/>
    public bool IsEnabled(string flagName, int? userId = null, int? companyId = null)
    {
        // First check the per-scope cache (populated by IsEnabledAsync)
        var cacheKey = BuildCacheKey(flagName, userId, companyId);
        if (_cache.TryGetValue(cacheKey, out bool cachedValue))
        {
            return cachedValue;
        }

        // Fall back to warm cache (bulk-loaded at startup via WarmCacheAsync)
        if (_cache.TryGetValue(WarmCacheKey, out List<FeatureFlag>? allFlags) && allFlags != null)
        {
            var resolved = ResolveFlagFromList(allFlags, flagName, userId, companyId);

            // Cache the resolved value in the per-scope cache for faster subsequent lookups
            _cache.Set(cacheKey, resolved, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheExpiration
            });

            return resolved;
        }

        // No cache available — return false (safe default, never hit DB synchronously)
        _logger.LogDebug("Feature flag {FlagName} not in warm cache, returning false (cache miss)", flagName);
        return false;
    }

    /// <inheritdoc/>
    public async Task WarmCacheAsync()
    {
        try
        {
            // SECURITY-AUDITED: SAFE — loads all flags into memory cache at startup; admin-only data
            var allFlags = await _context.FeatureFlags
                .IgnoreQueryFilters()
                .AsNoTracking()
                .ToListAsync();

            _cache.Set(WarmCacheKey, allFlags, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = WarmCacheExpiration
            });

            _logger.LogInformation("Feature flag warm cache loaded with {Count} flags", allFlags.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to warm feature flag cache. Sync IsEnabled() will return false for uncached flags.");
        }
    }

    /// <summary>
    /// Resolves the flag value from an in-memory list with priority: user-specific > company-specific > global.
    /// Used by the sync IsEnabled() method to avoid DB access.
    /// </summary>
    private static bool ResolveFlagFromList(List<FeatureFlag> allFlags, string flagName, int? userId, int? companyId)
    {
        var flags = allFlags.Where(f => f.Name == flagName).ToList();

        if (flags.Count == 0)
            return false;

        // Priority 1: User-specific flag (most specific)
        if (userId.HasValue && companyId.HasValue)
        {
            var userFlag = flags.FirstOrDefault(f => f.UserId == userId && f.CompanyId == companyId);
            if (userFlag != null)
                return userFlag.IsEnabled;
        }

        // Priority 2: Company-specific flag
        if (companyId.HasValue)
        {
            var companyFlag = flags.FirstOrDefault(f => f.CompanyId == companyId && f.UserId == null);
            if (companyFlag != null)
                return companyFlag.IsEnabled;
        }

        // Priority 3: Global flag (least specific)
        var globalFlag = flags.FirstOrDefault(f => f.CompanyId == null && f.UserId == null);
        if (globalFlag != null)
            return globalFlag.IsEnabled;

        return false;
    }

    /// <summary>
    /// Builds a cache key for a specific flag/scope combination.
    /// </summary>
    private static string BuildCacheKey(string flagName, int? userId, int? companyId)
    {
        return $"{CacheKeyPrefix}{flagName}_U{userId ?? 0}_C{companyId ?? 0}";
    }
}
