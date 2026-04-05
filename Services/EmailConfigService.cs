using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

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
        if (companyConfig != null) return companyConfig;

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
