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
        return await _context.EmailConfigs
            .FirstOrDefaultAsync(ec => ec.CompanyId == companyId);
    }

    public async Task<EmailConfig?> GetEmailConfigByCompanyIdAsync(int companyId)
    {
        // SECURITY-AUDITED: SAFE — scoped by specific companyId parameter
        return await _context.EmailConfigs
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ec => ec.CompanyId == companyId);
    }

    public async Task<EmailConfig> SaveEmailConfigAsync(
        bool enabled,
        string? apiKey,
        string? apiUrl,
        string? fromAddress,
        string updatedBy)
    {
        var companyId = _tenantResolver.GetCurrentTenantId();
        var config = await GetEmailConfigAsync();

        if (config == null)
        {
            // Create new configuration
            config = new EmailConfig
            {
                CompanyId = companyId,
                Enabled = enabled,
                ApiUrl = apiUrl,
                FromAddress = fromAddress,
                LastUpdated = DateTime.UtcNow,
                LastUpdatedBy = updatedBy
            };

            // Encrypt API key if provided
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                config.EncryptedApiKey = _encryptionService.Encrypt(apiKey);
            }

            _context.EmailConfigs.Add(config);
            _logger.LogInformation("Creating new email configuration for company {CompanyId}", companyId);
        }
        else
        {
            // Update existing configuration
            config.Enabled = enabled;
            config.ApiUrl = apiUrl;
            config.FromAddress = fromAddress;
            config.LastUpdated = DateTime.UtcNow;
            config.LastUpdatedBy = updatedBy;

            // Update API key only if a new one is provided
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                config.EncryptedApiKey = _encryptionService.Encrypt(apiKey);
                _logger.LogInformation("Updating API key for company {CompanyId}", companyId);
            }

            _logger.LogInformation("Updating email configuration for company {CompanyId}", companyId);
        }

        await _context.SaveChangesAsync();
        return config;
    }

    public async Task<string?> GetDecryptedApiKeyAsync()
    {
        var config = await GetEmailConfigAsync();
        if (config?.EncryptedApiKey == null)
        {
            return null;
        }

        try
        {
            return _encryptionService.Decrypt(config.EncryptedApiKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt API key for company {CompanyId}", config.CompanyId);
            throw;
        }
    }
}
