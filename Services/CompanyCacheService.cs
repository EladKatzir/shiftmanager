using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Service interface for caching company metadata
/// </summary>
public interface ICompanyCacheService
{
    Task<Company?> GetCompanyAsync(int companyId);
    void InvalidateCache(int companyId);
    Task<bool> IsSlugTakenAsync(string slug);
    Task<bool> HasOrphanedCompaniesAsync();
}

/// <summary>
/// Service for caching company metadata to reduce database queries
/// Caches company information for 10 minutes
/// </summary>
public class CompanyCacheService : ICompanyCacheService
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CompanyCacheService> _logger;

    private const int CacheDurationMinutes = 10;

    public CompanyCacheService(
        AppDbContext db,
        IMemoryCache cache,
        ILogger<CompanyCacheService> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Get company metadata, using cache when available
    /// </summary>
    public async Task<Company?> GetCompanyAsync(int companyId)
    {
        string cacheKey = $"Company_{companyId}";

        if (_cache.TryGetValue(cacheKey, out Company? company) && company != null)
        {
            _logger.LogDebug("Company cache hit for company {CompanyId}", companyId);
            return company;
        }

        _logger.LogDebug("Company cache miss for company {CompanyId}, loading from database", companyId);

        // Load from database
        company = await _db.Companies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == companyId);

        if (company == null)
        {
            _logger.LogWarning("Company {CompanyId} not found in database", companyId);
            return null;
        }

        // Cache for 10 minutes
        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(CacheDurationMinutes))
            .SetSize(1); // Estimate size for cache eviction policies

        _cache.Set(cacheKey, company, cacheOptions);

        _logger.LogInformation("Cached company {CompanyId} ({CompanyName})", companyId, company.DisplayName ?? company.Name);

        return company;
    }

    /// <summary>
    /// Invalidate the company cache
    /// Call this when company metadata is updated
    /// </summary>
    public void InvalidateCache(int companyId)
    {
        string cacheKey = $"Company_{companyId}";
        _cache.Remove(cacheKey);
        _logger.LogInformation("Invalidated Company cache for company {CompanyId}", companyId);
    }

    /// <summary>
    /// Check if a slug is already taken by any company (cross-tenant uniqueness check)
    /// </summary>
    // SECURITY-AUDITED: IgnoreQueryFilters() is SAFE — slug uniqueness must be checked across all tenants
    public async Task<bool> IsSlugTakenAsync(string slug)
    {
        return await _db.Companies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(c => c.Slug == slug);
    }

    /// <summary>
    /// Check if any companies have a null MoleculeId (orphaned companies health check)
    /// </summary>
    // SECURITY-AUDITED: IgnoreQueryFilters() is SAFE — system-wide health check for owner diagnostics
    public async Task<bool> HasOrphanedCompaniesAsync()
    {
        return await _db.Companies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(c => c.MoleculeId == null);
    }
}
