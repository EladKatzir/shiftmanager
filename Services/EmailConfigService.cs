using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>Row in the owner email-override dashboard table.</summary>
public record CompanyOverrideRow
{
    public int CompanyId { get; init; }
    public string CompanyName { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public bool OverrideEnabled { get; init; }
    public string? FromAddress { get; init; }
    public DateTime LastUpdated { get; init; }
    public string? LastUpdatedBy { get; init; }
}

/// <summary>
/// Service for managing email configuration settings with encryption support
/// </summary>
// SECURITY-AUDITED: IgnoreQueryFilters() in this class is SAFE — email config lookup scoped by explicit companyId;
// called only from Owner/Admin-protected configuration pages
public class EmailConfigService : IEmailConfigService
{
    private readonly AppDbContext _context;
    private readonly IEncryptionService _encryptionService;
    private readonly ITenantResolver _tenantResolver;
    private readonly ILogger<EmailConfigService> _logger;

    public EmailConfigService(
        AppDbContext context,
        IEncryptionService encryptionService,
        ITenantResolver tenantResolver,
        ILogger<EmailConfigService> logger)
    {
        _context = context;
        _encryptionService = encryptionService;
        _tenantResolver = tenantResolver;
        _logger = logger;
    }

    public async Task<EmailConfig?> GetEmailConfigAsync()
    {
        var companyId = _tenantResolver.GetCurrentTenantId();

        // Try company-specific config first (query filter already includes both global + tenant)
        var companyConfig = await _context.EmailConfigs
            .FirstOrDefaultAsync(ec => ec.CompanyId == companyId);
        if (companyConfig != null && companyConfig.OverrideEnabled) return companyConfig;

        // Fall back to global config (CompanyId = null)
        // SECURITY-AUDITED: SAFE — global config is intentionally shared, no tenant scoping
        return await _context.EmailConfigs
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ec => ec.CompanyId == null);
    }

    public async Task<EmailConfig?> GetGlobalEmailConfigAsync()
    {
        // SECURITY-AUDITED: SAFE — global config is intentionally shared, no tenant scoping
        return await _context.EmailConfigs
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ec => ec.CompanyId == null);
    }

    public async Task<EmailConfig?> GetEmailConfigByCompanyIdAsync(int companyId)
    {
        // SECURITY-AUDITED: SAFE — scoped by specific companyId parameter
        return await _context.EmailConfigs
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ec => ec.CompanyId == companyId);
    }

    public async Task<bool> HasCompanyOverrideAsync()
    {
        var companyId = _tenantResolver.GetCurrentTenantId();
        return await _context.EmailConfigs
            .AnyAsync(ec => ec.CompanyId == companyId);
    }

    public async Task<bool> DeleteCompanyOverrideAsync()
    {
        var companyId = _tenantResolver.GetCurrentTenantId();
        var config = await _context.EmailConfigs
            .FirstOrDefaultAsync(ec => ec.CompanyId == companyId);
        if (config == null) return false;

        _context.EmailConfigs.Remove(config);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Deleted company email config override for company {CompanyId}", companyId);
        return true;
    }

    public async Task<EmailConfig> SaveEmailConfigAsync(
        bool enabled,
        string? apiKey,
        string? apiUrl,
        string? fromAddress,
        string updatedBy)
    {
        var companyId = _tenantResolver.GetCurrentTenantId();
        // Only look for company-specific config (not global fallback)
        var config = await _context.EmailConfigs
            .FirstOrDefaultAsync(ec => ec.CompanyId == companyId);

        if (config == null)
        {
            config = new EmailConfig
            {
                CompanyId = companyId,
                Enabled = enabled,
                ApiUrl = apiUrl,
                FromAddress = fromAddress,
                LastUpdated = DateTime.UtcNow,
                LastUpdatedBy = updatedBy
            };

            if (!string.IsNullOrWhiteSpace(apiKey))
                config.EncryptedApiKey = _encryptionService.Encrypt(apiKey);

            _context.EmailConfigs.Add(config);
            _logger.LogInformation("Creating company email config override for company {CompanyId}", companyId);
        }
        else
        {
            config.Enabled = enabled;
            config.ApiUrl = apiUrl;
            config.FromAddress = fromAddress;
            config.LastUpdated = DateTime.UtcNow;
            config.LastUpdatedBy = updatedBy;

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                config.EncryptedApiKey = _encryptionService.Encrypt(apiKey);
            }

            _logger.LogInformation("Updating company email config for company {CompanyId}", companyId);
        }

        await _context.SaveChangesAsync();
        return config;
    }

    public async Task<EmailConfig> SaveGlobalEmailConfigAsync(
        bool enabled,
        string? apiKey,
        string? apiUrl,
        string? fromAddress,
        string updatedBy)
    {
        var config = await GetGlobalEmailConfigAsync();

        if (config == null)
        {
            config = new EmailConfig
            {
                CompanyId = null, // Global
                Enabled = enabled,
                ApiUrl = apiUrl,
                FromAddress = fromAddress,
                LastUpdated = DateTime.UtcNow,
                LastUpdatedBy = updatedBy
            };

            if (!string.IsNullOrWhiteSpace(apiKey))
                config.EncryptedApiKey = _encryptionService.Encrypt(apiKey);

            _context.EmailConfigs.Add(config);
            _logger.LogInformation("Creating global email configuration");
        }
        else
        {
            config.Enabled = enabled;
            config.ApiUrl = apiUrl;
            config.FromAddress = fromAddress;
            config.LastUpdated = DateTime.UtcNow;
            config.LastUpdatedBy = updatedBy;

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                config.EncryptedApiKey = _encryptionService.Encrypt(apiKey);
            }

            _logger.LogInformation("Updating global email configuration");
        }

        await _context.SaveChangesAsync();
        return config;
    }

    public async Task<List<CompanyOverrideRow>> GetAllCompanyOverridesWithNamesAsync()
    {
        // SECURITY-AUDITED: IgnoreQueryFilters() is SAFE — owner-only dashboard, intentional cross-tenant read
        var overrides = await _context.EmailConfigs
            .IgnoreQueryFilters()
            .Where(ec => ec.CompanyId != null)
            .OrderBy(ec => ec.CompanyId)
            .ToListAsync();

        if (overrides.Count == 0)
            return new List<CompanyOverrideRow>();

        var companyIds = overrides.Select(ec => ec.CompanyId!.Value).ToList();
        // SECURITY-AUDITED: IgnoreQueryFilters() is SAFE — fetching company names for owner dashboard
        var companyNames = await _context.Companies
            .IgnoreQueryFilters()
            .Where(c => companyIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c.Name);

        return overrides.Select(ec => new CompanyOverrideRow
        {
            CompanyId = ec.CompanyId!.Value,
            CompanyName = companyNames.TryGetValue(ec.CompanyId.Value, out var name) ? name : $"Company #{ec.CompanyId}",
            Enabled = ec.Enabled,
            OverrideEnabled = ec.OverrideEnabled,
            FromAddress = ec.FromAddress,
            LastUpdated = ec.LastUpdated,
            LastUpdatedBy = ec.LastUpdatedBy
        }).ToList();
    }

    public async Task<bool> SetOverrideEnabledAsync(int companyId, bool enabled)
    {
        // SECURITY-AUDITED: IgnoreQueryFilters() is SAFE — scoped by specific companyId, owner-only operation
        var config = await _context.EmailConfigs
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ec => ec.CompanyId == companyId);

        if (config == null) return false;

        config.OverrideEnabled = enabled;
        await _context.SaveChangesAsync();
        _logger.LogInformation("Set OverrideEnabled={Enabled} for company {CompanyId} email config", enabled, companyId);
        return true;
    }

    public async Task<string?> GetDecryptedApiKeyAsync()
    {
        var config = await GetEmailConfigAsync();
        if (config?.EncryptedApiKey == null)
            return null;

        try
        {
            return _encryptionService.Decrypt(config.EncryptedApiKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt API key for config {ConfigId} (CompanyId={CompanyId})",
                config.Id, config.CompanyId);
            throw;
        }
    }
}
