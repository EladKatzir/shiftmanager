using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Service for managing company-scoped localization overrides.
/// Provides caching and validation for custom translations.
/// </summary>
public interface ICompanyLocalizationService
{
    /// <summary>
    /// Get all active overrides for a company and culture.
    /// Results are cached for performance.
    /// </summary>
    Task<Dictionary<string, string>> GetOverridesAsync(int companyId, string culture);

    /// <summary>
    /// Get a specific override value for a company, culture, and resource key.
    /// Returns null if no override exists.
    /// </summary>
    Task<string?> GetOverrideValueAsync(int companyId, string culture, string resourceKey);

    /// <summary>
    /// Get a specific override entity (for editing/viewing details)
    /// </summary>
    Task<CompanyLocalizationOverride?> GetOverrideAsync(int companyId, string culture, string resourceKey);

    /// <summary>
    /// Search overrides by culture and/or search term (searches key and value)
    /// </summary>
    Task<List<CompanyLocalizationOverride>> SearchOverridesAsync(int companyId, string? culture = null, string? searchTerm = null);

    /// <summary>
    /// Create or update a single override.
    /// Value will be HTML-encoded for security.
    /// Validates placeholder preservation for parameterized strings.
    /// </summary>
    Task UpsertOverrideAsync(int companyId, string culture, string resourceKey, string overrideValue, int userId);

    /// <summary>
    /// Bulk upsert overrides (used for "Save and Exit" in edit mode).
    /// Validates all overrides before committing.
    /// </summary>
    Task UpsertOverridesBulkAsync(int companyId, string culture, Dictionary<string, string> overrides, int userId);

    /// <summary>
    /// Delete an override (soft delete by setting IsActive = false)
    /// </summary>
    Task DeleteOverrideAsync(int companyId, string culture, string resourceKey, int userId);

    /// <summary>
    /// Invalidate cache for a specific company and culture
    /// </summary>
    void InvalidateCache(int companyId, string culture);

    /// <summary>
    /// Invalidate all cached overrides for a company
    /// </summary>
    void InvalidateCacheForCompany(int companyId);
}
