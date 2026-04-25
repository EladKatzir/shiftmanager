using ShiftManager.Models;
using ShiftManager.Models.Results;

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
    /// <param name="flagName">The name of the feature flag (e.g., "FF_WIDGETS_ENABLED")</param>
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

    // ==================== Diagnostic (OperationResult) variants ====================
    //
    // These methods are ADDITIVE companions to the Task<bool> APIs above. They wrap
    // the same underlying flag-resolution logic but return a structured
    // <see cref="OperationResult{T}"/> so callers can distinguish "flag is off" from
    // "we could not check the flag because the database is down".
    //
    // The audit flagged FeatureFlagService for "silent fallback to false on DB error".
    // Routine flag checks (30+ callers) intentionally keep the bool contract because
    // flags are inherently on/off — a false on transient DB failure is acceptable for
    // gate logic. Admin and diagnostic surfaces, however, need to surface the error
    // so operators know flags are degraded. Those surfaces use the Try* methods below.

    /// <summary>
    /// Diagnostic variant of <see cref="IsEnabledAsync"/> that returns a structured
    /// <see cref="OperationResult{T}"/>.
    /// <list type="bullet">
    ///   <item><c>Ok(true)</c> / <c>Ok(false)</c> on a successful resolution.</item>
    ///   <item><c>Fail("Error_FeatureFlagService_DatabaseError", ...)</c> when the
    ///   underlying database query throws. The non-diagnostic <see cref="IsEnabledAsync"/>
    ///   silently treats the flag as disabled in that case; this method makes the
    ///   failure visible to admin/diagnostic callers.</item>
    /// </list>
    /// </summary>
    /// <param name="flagName">The name of the feature flag (e.g., "FF_WIDGETS_ENABLED")</param>
    /// <param name="userId">Optional user ID for user-specific check</param>
    /// <param name="companyId">Optional company ID for company-specific check</param>
    Task<OperationResult<bool>> TryIsEnabledAsync(string flagName, int? userId = null, int? companyId = null);

    /// <summary>
    /// Company-scoped diagnostic variant of <see cref="IsEnabledAsync"/>. Same semantics
    /// as <see cref="TryIsEnabledAsync"/>, but pre-binds <paramref name="companyId"/> for
    /// admin pages that already have a company context.
    /// </summary>
    Task<OperationResult<bool>> TryIsEnabledForCompanyAsync(string flagName, int companyId);

    /// <summary>
    /// Diagnostic variant of <see cref="GetAllFlagsAsync"/> intended for admin pages.
    /// Returns <c>Ok(list)</c> on success, or
    /// <c>Fail("Error_FeatureFlagService_DatabaseError", ...)</c> if the underlying
    /// database query throws. Unlike <see cref="GetAllFlagsAsync"/>, callers can
    /// distinguish "no flags configured" from "could not load flags".
    /// </summary>
    Task<OperationResult<List<FeatureFlag>>> TryListAllFlagsAsync();
}
