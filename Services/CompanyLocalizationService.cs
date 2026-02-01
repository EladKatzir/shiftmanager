using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Resources;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ShiftManager.Services;

/// <summary>
/// Service for managing company-scoped localization overrides with caching and validation.
/// </summary>
public class CompanyLocalizationService : ICompanyLocalizationService
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CompanyLocalizationService> _logger;
    private readonly IAuditLogService _auditLogService;
    private readonly IStringLocalizer<SharedResources> _baseLocalizer;

    public CompanyLocalizationService(
        AppDbContext db,
        IMemoryCache cache,
        ILogger<CompanyLocalizationService> logger,
        IAuditLogService auditLogService,
        IStringLocalizer<SharedResources> baseLocalizer)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
        _auditLogService = auditLogService;
        _baseLocalizer = baseLocalizer;
    }

    /// <summary>
    /// Get cache key for a company and culture
    /// </summary>
    private string GetCacheKey(int companyId, string culture) => $"localization:{companyId}:{culture}";

    public async Task<Dictionary<string, string>> GetOverridesAsync(int companyId, string culture)
    {
        var cacheKey = GetCacheKey(companyId, culture);

        // Try cache first
        if (_cache.TryGetValue(cacheKey, out Dictionary<string, string>? cached))
        {
            _logger.LogDebug("Cache HIT for {CacheKey}", cacheKey);
            return cached!;
        }

        // Load from DB
        var overrides = await _db.CompanyLocalizationOverrides
            .Where(o => o.CompanyId == companyId && o.Culture == culture && o.IsActive)
            .Select(o => new { o.ResourceKey, o.OverrideValue })
            .ToDictionaryAsync(o => o.ResourceKey, o => o.OverrideValue);

        // Cache for 1 hour (long-lived, invalidated on updates)
        _cache.Set(cacheKey, overrides, TimeSpan.FromHours(1));

        _logger.LogInformation("Loaded {Count} overrides for CompanyId={CompanyId}, Culture={Culture}",
            overrides.Count, companyId, culture);

        return overrides;
    }

    public async Task<string?> GetOverrideValueAsync(int companyId, string culture, string resourceKey)
    {
        var overrides = await GetOverridesAsync(companyId, culture);
        return overrides.TryGetValue(resourceKey, out var value) ? value : null;
    }

    public async Task<CompanyLocalizationOverride?> GetOverrideAsync(int companyId, string culture, string resourceKey)
    {
        return await _db.CompanyLocalizationOverrides
            .FirstOrDefaultAsync(o => o.CompanyId == companyId && o.Culture == culture && o.ResourceKey == resourceKey && o.IsActive);
    }

    public async Task<List<CompanyLocalizationOverride>> SearchOverridesAsync(int companyId, string? culture = null, string? searchTerm = null)
    {
        var query = _db.CompanyLocalizationOverrides
            .Where(o => o.CompanyId == companyId && o.IsActive);

        if (!string.IsNullOrWhiteSpace(culture))
        {
            query = query.Where(o => o.Culture == culture);
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.ToLower();
            query = query.Where(o => o.ResourceKey.ToLower().Contains(term) || o.OverrideValue.ToLower().Contains(term));
        }

        return await query
            .OrderBy(o => o.Culture)
            .ThenBy(o => o.ResourceKey)
            .ToListAsync();
    }

    public async Task UpsertOverrideAsync(int companyId, string culture, string resourceKey, string overrideValue, int userId)
    {
        // Validate inputs
        ValidateCulture(culture);
        ValidateResourceKey(resourceKey);
        ValidateOverrideValue(overrideValue, resourceKey);

        var existing = await _db.CompanyLocalizationOverrides
            .FirstOrDefaultAsync(o => o.CompanyId == companyId && o.Culture == culture && o.ResourceKey == resourceKey);

        if (existing != null)
        {
            // Update existing
            var oldValue = existing.OverrideValue;
            existing.OverrideValue = WebUtility.HtmlEncode(overrideValue); // SECURITY: HTML encode
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedBy = userId;
            existing.IsActive = true;

            await _db.SaveChangesAsync();

            // Audit log
            await _auditLogService.LogUserActionAsync(
                userId, "LocalizationOverrideUpdated", "CompanyLocalizationOverride", existing.Id,
                $"Updated override for key '{resourceKey}'",
                JsonSerializer.Serialize(new { culture, resourceKey, oldValue, newValue = overrideValue }));

            _logger.LogInformation("Updated override: CompanyId={CompanyId}, Culture={Culture}, Key={Key}",
                companyId, culture, resourceKey);
        }
        else
        {
            // Create new
            var newOverride = new CompanyLocalizationOverride
            {
                CompanyId = companyId,
                Culture = culture,
                ResourceKey = resourceKey,
                OverrideValue = WebUtility.HtmlEncode(overrideValue),
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userId,
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = userId
            };

            _db.CompanyLocalizationOverrides.Add(newOverride);
            await _db.SaveChangesAsync();

            // Audit log
            await _auditLogService.LogUserActionAsync(
                userId, "LocalizationOverrideCreated", "CompanyLocalizationOverride", newOverride.Id,
                $"Created override for key '{resourceKey}'",
                JsonSerializer.Serialize(new { culture, resourceKey, value = overrideValue }));

            _logger.LogInformation("Created override: CompanyId={CompanyId}, Culture={Culture}, Key={Key}",
                companyId, culture, resourceKey);
        }

        // CRITICAL: Invalidate cache
        InvalidateCache(companyId, culture);
    }

    public async Task UpsertOverridesBulkAsync(int companyId, string culture, Dictionary<string, string> overrides, int userId)
    {
        _logger.LogInformation("Bulk upserting {Count} overrides for CompanyId={CompanyId}, Culture={Culture}",
            overrides.Count, companyId, culture);

        // Validate all overrides first (fail fast if any are invalid)
        ValidateCulture(culture);
        foreach (var kvp in overrides)
        {
            ValidateResourceKey(kvp.Key);
            ValidateOverrideValue(kvp.Value, kvp.Key);
        }

        // Load all existing overrides for this company/culture
        var existingOverrides = await _db.CompanyLocalizationOverrides
            .Where(o => o.CompanyId == companyId && o.Culture == culture)
            .ToDictionaryAsync(o => o.ResourceKey, o => o);

        int updatedCount = 0;
        int createdCount = 0;

        foreach (var kvp in overrides)
        {
            var resourceKey = kvp.Key;
            var overrideValue = WebUtility.HtmlEncode(kvp.Value);

            if (existingOverrides.TryGetValue(resourceKey, out var existing))
            {
                // Update existing
                existing.OverrideValue = overrideValue;
                existing.UpdatedAt = DateTime.UtcNow;
                existing.UpdatedBy = userId;
                existing.IsActive = true;
                updatedCount++;
            }
            else
            {
                // Create new
                var newOverride = new CompanyLocalizationOverride
                {
                    CompanyId = companyId,
                    Culture = culture,
                    ResourceKey = resourceKey,
                    OverrideValue = overrideValue,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = userId,
                    UpdatedAt = DateTime.UtcNow,
                    UpdatedBy = userId
                };
                _db.CompanyLocalizationOverrides.Add(newOverride);
                createdCount++;
            }
        }

        await _db.SaveChangesAsync();

        // Audit log
        await _auditLogService.LogUserActionAsync(
            userId, "LocalizationOverridesBulkUpsert", "CompanyLocalizationOverride", null,
            $"Bulk upserted {overrides.Count} overrides ({createdCount} created, {updatedCount} updated)",
            JsonSerializer.Serialize(new { companyId, culture, count = overrides.Count, created = createdCount, updated = updatedCount }));

        _logger.LogInformation("Bulk upsert complete: {Created} created, {Updated} updated",
            createdCount, updatedCount);

        // Invalidate cache
        InvalidateCache(companyId, culture);
    }

    public async Task DeleteOverrideAsync(int companyId, string culture, string resourceKey, int userId)
    {
        var existing = await _db.CompanyLocalizationOverrides
            .FirstOrDefaultAsync(o => o.CompanyId == companyId && o.Culture == culture && o.ResourceKey == resourceKey && o.IsActive);

        if (existing == null)
        {
            _logger.LogWarning("Attempted to delete non-existent override: CompanyId={CompanyId}, Culture={Culture}, Key={Key}",
                companyId, culture, resourceKey);
            return;
        }

        // Soft delete
        existing.IsActive = false;
        existing.UpdatedAt = DateTime.UtcNow;
        existing.UpdatedBy = userId;

        await _db.SaveChangesAsync();

        // Audit log
        await _auditLogService.LogUserActionAsync(
            userId, "LocalizationOverrideDeleted", "CompanyLocalizationOverride", existing.Id,
            $"Deleted override for key '{resourceKey}'",
            JsonSerializer.Serialize(new { culture, resourceKey, value = existing.OverrideValue }));

        _logger.LogInformation("Deleted override: CompanyId={CompanyId}, Culture={Culture}, Key={Key}",
            companyId, culture, resourceKey);

        // Invalidate cache
        InvalidateCache(companyId, culture);
    }

    public void InvalidateCache(int companyId, string culture)
    {
        var cacheKey = GetCacheKey(companyId, culture);
        _cache.Remove(cacheKey);
        _logger.LogInformation("Cache invalidated for {CacheKey}", cacheKey);
    }

    public void InvalidateCacheForCompany(int companyId)
    {
        // Invalidate both cultures
        InvalidateCache(companyId, "en-US");
        InvalidateCache(companyId, "he-IL");
    }

    // Validation methods

    private void ValidateCulture(string culture)
    {
        if (culture != "en-US" && culture != "he-IL")
        {
            throw new ArgumentException($"Invalid culture '{culture}'. Must be 'en-US' or 'he-IL'.");
        }
    }

    private void ValidateResourceKey(string resourceKey)
    {
        if (string.IsNullOrWhiteSpace(resourceKey))
        {
            throw new ArgumentException("ResourceKey cannot be empty.");
        }

        if (resourceKey.Length > 200)
        {
            throw new ArgumentException("ResourceKey must be 200 characters or less.");
        }

        if (!Regex.IsMatch(resourceKey, @"^[A-Za-z0-9_]+$"))
        {
            throw new ArgumentException("ResourceKey must contain only alphanumeric characters and underscores.");
        }
    }

    private void ValidateOverrideValue(string value, string resourceKey)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("OverrideValue cannot be empty.");
        }

        if (value.Length > 2000)
        {
            throw new ArgumentException("OverrideValue must be 2000 characters or less.");
        }

        // Check for control characters (security)
        if (value.Any(c => char.IsControl(c) && c != '\r' && c != '\n' && c != '\t'))
        {
            throw new ArgumentException("OverrideValue contains invalid control characters.");
        }

        // Validate parameterized strings preserve placeholders
        try
        {
            var baseValue = _baseLocalizer[resourceKey].Value;
            if (!string.IsNullOrEmpty(baseValue) && baseValue != resourceKey) // resourceKey means not found
            {
                var basePlaceholders = ExtractPlaceholders(baseValue);
                var overridePlaceholders = ExtractPlaceholders(value);

                if (!basePlaceholders.SetEquals(overridePlaceholders))
                {
                    throw new ArgumentException(
                        $"Override must preserve placeholders. Expected: {{{string.Join(", ", basePlaceholders.Order())}}}, " +
                        $"Got: {{{string.Join(", ", overridePlaceholders.Order())}}}");
                }
            }
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            // Log but don't fail if we can't validate placeholders
            _logger.LogWarning(ex, "Failed to validate placeholders for key {Key}", resourceKey);
        }
    }

    private HashSet<string> ExtractPlaceholders(string text)
    {
        var matches = Regex.Matches(text, @"\{(\d+)\}");
        return matches.Select(m => m.Value).ToHashSet();
    }
}
