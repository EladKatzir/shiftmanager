using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Service for managing feature flags with support for global, company, and user scoping.
/// Flag state is cached for 1 minute for performance.
/// </summary>
public interface IFeatureFlagService
{
    /// <summary>
    /// Checks if a feature flag is enabled for the given scope.
    /// Resolution priority: user-specific > company-specific > global.
    /// </summary>
    /// <param name="flagName">The name of the feature flag (e.g., "FF_NEW_NAV_ENABLED")</param>
    /// <param name="userId">Optional user ID for user-specific check</param>
    /// <param name="companyId">Optional company ID for company-specific check</param>
    /// <returns>True if the flag is enabled, false otherwise</returns>
    Task<bool> IsEnabledAsync(string flagName, int? userId = null, int? companyId = null);

    /// <summary>
    /// Sets a feature flag's enabled state.
    /// </summary>
    /// <param name="flagName">The name of the feature flag</param>
    /// <param name="isEnabled">Whether the flag should be enabled</param>
    /// <param name="companyId">Optional company ID for company-scoped flag</param>
    /// <param name="userId">Optional user ID for user-scoped flag</param>
    /// <param name="description">Optional description for the flag</param>
    Task SetFlagAsync(string flagName, bool isEnabled, int? companyId = null, int? userId = null, string? description = null);

    /// <summary>
    /// Gets all feature flags, optionally filtered by company.
    /// </summary>
    /// <param name="companyId">Optional company ID to filter by</param>
    /// <returns>All matching feature flags</returns>
    Task<IEnumerable<FeatureFlag>> GetAllFlagsAsync(int? companyId = null);

    /// <summary>
    /// Gets a specific feature flag by name and scope.
    /// </summary>
    /// <param name="flagName">The name of the feature flag</param>
    /// <param name="companyId">Optional company ID</param>
    /// <param name="userId">Optional user ID</param>
    /// <returns>The feature flag if found, null otherwise</returns>
    Task<FeatureFlag?> GetFlagAsync(string flagName, int? companyId = null, int? userId = null);

    /// <summary>
    /// Deletes a feature flag.
    /// </summary>
    /// <param name="flagId">The ID of the flag to delete</param>
    Task DeleteFlagAsync(int flagId);

    /// <summary>
    /// Invalidates the cache for a specific flag scope.
    /// </summary>
    void InvalidateCache(string flagName, int? companyId = null, int? userId = null);

    /// <summary>
    /// Synchronous cache-only check for whether a feature flag is enabled.
    /// Returns false if the flag is not in the warm cache (never hits DB synchronously).
    /// Safe for use in Razor views, interceptors, and synchronous code paths.
    /// Call WarmCacheAsync() at startup to ensure flags are available.
    /// </summary>
    /// <param name="flagName">The name of the feature flag (e.g., "FF_ALLOW_PUBLIC_SIGNUP")</param>
    /// <param name="userId">Optional user ID for user-specific check</param>
    /// <param name="companyId">Optional company ID for company-specific check</param>
    /// <returns>True if the flag is enabled in cache, false if disabled or not cached</returns>
    bool IsEnabled(string flagName, int? userId = null, int? companyId = null);

    /// <summary>
    /// Warms the cache by loading ALL feature flags from the database.
    /// Should be called once at application startup after app.Build().
    /// This ensures the sync IsEnabled() method has data to work with immediately.
    /// </summary>
    Task WarmCacheAsync();
}
