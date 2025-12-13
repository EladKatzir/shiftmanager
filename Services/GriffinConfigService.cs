using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

public class GriffinConfigService : IGriffinConfigService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantResolver _tenantResolver;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GriffinConfigService> _logger;

    public GriffinConfigService(
        AppDbContext dbContext,
        ITenantResolver tenantResolver,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<GriffinConfigService> logger)
    {
        _dbContext = dbContext;
        _tenantResolver = tenantResolver;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<GriffinConfig?> GetGriffinConfigAsync()
    {
        try
        {
            var companyId = _tenantResolver.GetCurrentTenantId();
            return await GetGriffinConfigByCompanyIdAsync(companyId);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to get Griffin config via tenant resolver, falling back to appsettings");
            return GetConfigFromAppSettings();
        }
    }

    public async Task<GriffinConfig?> GetGriffinConfigByCompanyIdAsync(int companyId)
    {
        // Try database first
        var dbConfig = await _dbContext.GriffinConfigs
            .FirstOrDefaultAsync(c => c.CompanyId == companyId);

        if (dbConfig != null)
        {
            _logger.LogDebug("Loaded Griffin config from database for company {CompanyId}", companyId);
            return dbConfig;
        }

        // Fallback to appsettings
        _logger.LogDebug("No Griffin config in database for company {CompanyId}, using appsettings fallback", companyId);
        var fallbackConfig = GetConfigFromAppSettings();

        if (fallbackConfig != null)
        {
            fallbackConfig.CompanyId = companyId;
        }

        return fallbackConfig;
    }

    public async Task<GriffinConfig> SaveGriffinConfigAsync(
        bool enabled,
        string? baseUrl,
        string? tokenConsumerUrl,
        bool autoProvisionUsers,
        UserRole defaultProvisionedRole,
        int timeoutSeconds,
        string updatedBy)
    {
        var companyId = _tenantResolver.GetCurrentTenantId();

        var config = await _dbContext.GriffinConfigs
            .FirstOrDefaultAsync(c => c.CompanyId == companyId);

        if (config == null)
        {
            // Create new
            config = new GriffinConfig
            {
                CompanyId = companyId,
                Enabled = enabled,
                BaseUrl = baseUrl,
                TokenConsumerUrl = tokenConsumerUrl,
                AutoProvisionUsers = autoProvisionUsers,
                DefaultProvisionedRole = defaultProvisionedRole,
                TimeoutSeconds = timeoutSeconds,
                LastUpdated = DateTime.UtcNow,
                LastUpdatedBy = updatedBy
            };

            _dbContext.GriffinConfigs.Add(config);
            _logger.LogInformation("Created new Griffin config for company {CompanyId}", companyId);
        }
        else
        {
            // Update existing
            config.Enabled = enabled;
            config.BaseUrl = baseUrl;
            config.TokenConsumerUrl = tokenConsumerUrl;
            config.AutoProvisionUsers = autoProvisionUsers;
            config.DefaultProvisionedRole = defaultProvisionedRole;
            config.TimeoutSeconds = timeoutSeconds;
            config.LastUpdated = DateTime.UtcNow;
            config.LastUpdatedBy = updatedBy;

            _logger.LogInformation("Updated Griffin config for company {CompanyId}", companyId);
        }

        await _dbContext.SaveChangesAsync();

        return config;
    }

    public async Task<bool> TestConnectionAsync(string baseUrl, int timeoutSeconds)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            // Test if Griffin /authentication endpoint responds
            var testUrl = $"{baseUrl.TrimEnd('/')}/authentication?tokenConsumerURL=test";
            var response = await client.GetAsync(testUrl);

            // We expect a redirect or 200, not 404/500
            var isAvailable = response.IsSuccessStatusCode ||
                             response.StatusCode == System.Net.HttpStatusCode.Redirect ||
                             response.StatusCode == System.Net.HttpStatusCode.Found ||
                             response.StatusCode == System.Net.HttpStatusCode.MovedPermanently;

            _logger.LogInformation("Griffin connection test to {BaseUrl}: {Result}", baseUrl, isAvailable ? "Success" : "Failed");

            return isAvailable;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Griffin connection test failed for {BaseUrl}", baseUrl);
            return false;
        }
    }

    private GriffinConfig? GetConfigFromAppSettings()
    {
        var enabled = _configuration.GetValue<bool>("Griffin:Enabled", false);
        var baseUrl = _configuration["Griffin:BaseUrl"];
        var tokenConsumerUrl = _configuration["Griffin:TokenConsumerUrl"];

        if (!enabled || string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        var autoProvisionUsers = _configuration.GetValue<bool>("Griffin:AutoProvisionUsers", true);
        var defaultRoleString = _configuration["Griffin:DefaultProvisionedRole"] ?? "Employee";
        var defaultRole = Enum.TryParse<UserRole>(defaultRoleString, out var role) ? role : UserRole.Employee;
        var timeoutSeconds = _configuration.GetValue<int>("Griffin:TimeoutSeconds", 10);

        return new GriffinConfig
        {
            Enabled = enabled,
            BaseUrl = baseUrl,
            TokenConsumerUrl = tokenConsumerUrl,
            AutoProvisionUsers = autoProvisionUsers,
            DefaultProvisionedRole = defaultRole,
            TimeoutSeconds = timeoutSeconds
        };
    }
}
