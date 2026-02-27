using System.Diagnostics;
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
    private readonly IGriffinApiLogService _griffinApiLogService;
    private readonly ILogger<GriffinConfigService> _logger;

    public GriffinConfigService(
        AppDbContext dbContext,
        ITenantResolver tenantResolver,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        IGriffinApiLogService griffinApiLogService,
        ILogger<GriffinConfigService> logger)
    {
        _dbContext = dbContext;
        _tenantResolver = tenantResolver;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _griffinApiLogService = griffinApiLogService;
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
            _logger.LogDebug(ex, "Unable to get Griffin config via tenant resolver, trying database fallback then appsettings");

            // Try to find any enabled config in the database (for unauthenticated users on the login page)
            var anyConfig = await GetAnyEnabledGriffinConfigAsync();
            if (anyConfig != null)
            {
                _logger.LogDebug("Found enabled Griffin config from database (CompanyId={CompanyId}) without tenant context", anyConfig.CompanyId);
                return anyConfig;
            }

            return GetConfigFromAppSettings();
        }
    }

    public async Task<GriffinConfig?> GetAnyEnabledGriffinConfigAsync()
    {
        try
        {
            // SECURITY-AUDITED: IgnoreQueryFilters needed — called from login page before tenant context exists;
            // returns first enabled config to determine if Griffin SSO is available at all
            var config = await _dbContext.GriffinConfigs
                .IgnoreQueryFilters()
                .Where(c => c.Enabled && !string.IsNullOrEmpty(c.BaseUrl) && !string.IsNullOrEmpty(c.TokenConsumerUrl))
                .FirstOrDefaultAsync();

            if (config != null)
            {
                _logger.LogDebug("Found enabled Griffin config in database (CompanyId={CompanyId}, BaseUrl={BaseUrl})",
                    config.CompanyId, config.BaseUrl);
            }
            else
            {
                _logger.LogDebug("No enabled Griffin config found in database");
            }

            return config;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error querying database for any enabled Griffin config");
            return null;
        }
    }

    public async Task<GriffinConfig?> GetGriffinConfigByCompanyIdAsync(int companyId)
    {
        // Try database first
        // SECURITY-AUDITED: IgnoreQueryFilters needed — method called during SSO auth before tenant context exists; scoped by explicit companyId
        var dbConfig = await _dbContext.GriffinConfigs
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.CompanyId == companyId);

        if (dbConfig != null)
        {
            _logger.LogDebug("Loaded Griffin config from database for company {CompanyId}:", companyId);
            _logger.LogDebug("  - Enabled: {Enabled}", dbConfig.Enabled);
            _logger.LogDebug("  - BaseUrl: {BaseUrl}", dbConfig.BaseUrl ?? "(null)");
            _logger.LogDebug("  - TokenConsumerUrl: {TokenConsumerUrl}", dbConfig.TokenConsumerUrl ?? "(null)");
            _logger.LogDebug("  - AutoProvisionUsers: {AutoProvision}", dbConfig.AutoProvisionUsers);
            _logger.LogDebug("  - DefaultProvisionedRole: {Role}", dbConfig.DefaultProvisionedRole);
            _logger.LogDebug("  - TimeoutSeconds: {Timeout}", dbConfig.TimeoutSeconds);
            return dbConfig;
        }

        // Fallback to appsettings
        _logger.LogDebug("No Griffin config in database for company {CompanyId}, using appsettings fallback", companyId);
        var fallbackConfig = GetConfigFromAppSettings();

        if (fallbackConfig != null)
        {
            fallbackConfig.CompanyId = companyId;
            _logger.LogDebug("Fallback config from appsettings.json:");
            _logger.LogDebug("  - Enabled: {Enabled}", fallbackConfig.Enabled);
            _logger.LogDebug("  - BaseUrl: {BaseUrl}", fallbackConfig.BaseUrl ?? "(null)");
            _logger.LogDebug("  - TokenConsumerUrl: {TokenConsumerUrl}", fallbackConfig.TokenConsumerUrl ?? "(null)");
        }
        else
        {
            _logger.LogDebug("No fallback config available from appsettings.json (Enabled=false or BaseUrl missing)");
        }

        return fallbackConfig;
    }

    public async Task<GriffinConfig> SaveGriffinConfigAsync(
        bool enabled,
        string? baseUrl,
        string? tokenConsumerUrl,
        bool autoProvisionUsers,
        UserRole defaultProvisionedRole,
        int? defaultProvisionedRoleTemplateId,
        int timeoutSeconds,
        string updatedBy)
    {
        var companyId = _tenantResolver.GetCurrentTenantId();

        // SECURITY-AUDITED: IgnoreQueryFilters — SaveAsync called from Owner page (already tenant-scoped); explicit companyId filter
        var config = await _dbContext.GriffinConfigs
            .IgnoreQueryFilters()
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
                DefaultProvisionedRoleTemplateId = defaultProvisionedRoleTemplateId,
                TimeoutSeconds = timeoutSeconds,
                LastUpdated = DateTime.UtcNow,
                LastUpdatedBy = updatedBy
            };

            _dbContext.GriffinConfigs.Add(config);
            _logger.LogInformation("Created new Griffin config for company {CompanyId}:", companyId);
            _logger.LogInformation("  - Enabled: {Enabled}", enabled);
            _logger.LogInformation("  - BaseUrl: {BaseUrl}", baseUrl ?? "(null)");
            _logger.LogInformation("  - TokenConsumerUrl: {TokenConsumerUrl}", tokenConsumerUrl ?? "(null)");
            _logger.LogInformation("  - AutoProvisionUsers: {AutoProvision}", autoProvisionUsers);
            _logger.LogInformation("  - DefaultProvisionedRole: {Role}", defaultProvisionedRole);
            _logger.LogInformation("  - UpdatedBy: {User}", updatedBy);
        }
        else
        {
            // Update existing
            config.Enabled = enabled;
            config.BaseUrl = baseUrl;
            config.TokenConsumerUrl = tokenConsumerUrl;
            config.AutoProvisionUsers = autoProvisionUsers;
            config.DefaultProvisionedRole = defaultProvisionedRole;
            config.DefaultProvisionedRoleTemplateId = defaultProvisionedRoleTemplateId;
            config.TimeoutSeconds = timeoutSeconds;
            config.LastUpdated = DateTime.UtcNow;
            config.LastUpdatedBy = updatedBy;

            _logger.LogInformation("Updated Griffin config for company {CompanyId}:", companyId);
            _logger.LogInformation("  - Enabled: {Enabled}", enabled);
            _logger.LogInformation("  - BaseUrl: {BaseUrl}", baseUrl ?? "(null)");
            _logger.LogInformation("  - TokenConsumerUrl: {TokenConsumerUrl}", tokenConsumerUrl ?? "(null)");
            _logger.LogInformation("  - AutoProvisionUsers: {AutoProvision}", autoProvisionUsers);
            _logger.LogInformation("  - DefaultProvisionedRole: {Role}", defaultProvisionedRole);
            _logger.LogInformation("  - UpdatedBy: {User}", updatedBy);
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Griffin config saved successfully to database for company {CompanyId}", companyId);

        return config;
    }

    public async Task<GriffinConnectionTestResult> TestConnectionAsync(string baseUrl, int timeoutSeconds)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new GriffinConnectionTestResult();
        var validationErrors = new List<string>();

        try
        {
            // Step 1: Validate URL format
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                validationErrors.Add("Base URL cannot be empty");
                result.ValidationErrors = validationErrors;
                result.ErrorMessage = "Invalid configuration: Base URL is required";
                result.DurationMs = (int)stopwatch.ElapsedMilliseconds;

                // Log the validation failure
                await _griffinApiLogService.LogConnectionTestAsync(
                    baseUrl ?? "null",
                    "GET",
                    new Dictionary<string, string>(),
                    null,
                    null,
                    null,
                    null,
                    false,
                    result.ErrorMessage,
                    result.DurationMs,
                    validationErrors);

                return result;
            }

            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                validationErrors.Add("Base URL must be a valid HTTP or HTTPS URL");
                result.ValidationErrors = validationErrors;
                result.ErrorMessage = "Invalid URL format";
                result.DurationMs = (int)stopwatch.ElapsedMilliseconds;

                // Log the validation failure
                await _griffinApiLogService.LogConnectionTestAsync(
                    baseUrl,
                    "GET",
                    new Dictionary<string, string>(),
                    null,
                    null,
                    null,
                    null,
                    false,
                    result.ErrorMessage,
                    result.DurationMs,
                    validationErrors);

                return result;
            }

            // Step 2: Make HTTP request to Griffin
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            var testUrl = $"{baseUrl.TrimEnd('/')}/authentication?tokenConsumerURL=test";
            var requestHeaders = new Dictionary<string, string>();

            _logger.LogDebug("Testing Griffin connection to: {TestUrl}", testUrl);

            var response = await client.GetAsync(testUrl);

            stopwatch.Stop();
            result.DurationMs = (int)stopwatch.ElapsedMilliseconds;

            // Step 3: Capture response details
            result.StatusCode = (int)response.StatusCode;

            // Capture response headers
            var responseHeaders = new Dictionary<string, string>();
            foreach (var header in response.Headers)
            {
                responseHeaders[header.Key] = string.Join(", ", header.Value);
            }
            foreach (var header in response.Content.Headers)
            {
                responseHeaders[header.Key] = string.Join(", ", header.Value);
            }
            result.ResponseHeaders = responseHeaders;

            // Capture redirect URL if applicable
            if (response.Headers.Location != null)
            {
                result.RedirectUrl = response.Headers.Location.ToString();
            }

            // Capture response body (limited to 2000 chars for display)
            var responseBody = await response.Content.ReadAsStringAsync();
            result.ResponseBody = responseBody.Length > 2000
                ? responseBody.Substring(0, 2000) + "... (truncated)"
                : responseBody;

            // Step 4: Determine success
            // We expect a redirect (302/307) or success (200), not 404/500
            var isSuccess = response.IsSuccessStatusCode ||
                           response.StatusCode == System.Net.HttpStatusCode.Redirect ||
                           response.StatusCode == System.Net.HttpStatusCode.Found ||
                           response.StatusCode == System.Net.HttpStatusCode.MovedPermanently ||
                           response.StatusCode == System.Net.HttpStatusCode.TemporaryRedirect;

            result.Success = isSuccess;

            if (!isSuccess)
            {
                result.ErrorMessage = $"Unexpected HTTP status: {result.StatusCode} ({response.StatusCode})";
            }

            _logger.LogInformation(
                "Griffin connection test completed: {Success}, Status: {StatusCode}, Duration: {Duration}ms",
                isSuccess ? "SUCCESS" : "FAILURE",
                result.StatusCode,
                result.DurationMs);

            // Step 5: Log to database (fire-and-forget pattern like EmailApiLogService)
            _ = _griffinApiLogService.LogConnectionTestAsync(
                testUrl,
                "GET",
                requestHeaders,
                result.StatusCode,
                responseHeaders,
                result.ResponseBody,
                result.RedirectUrl,
                result.Success,
                result.ErrorMessage,
                result.DurationMs,
                null);

            return result;
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            result.DurationMs = (int)stopwatch.ElapsedMilliseconds;
            result.Success = false;
            result.ErrorMessage = $"Network error: {ex.Message}";

            _logger.LogError(ex, "Griffin connection test failed: Network error for {BaseUrl}", baseUrl);

            // Log the failure
            _ = _griffinApiLogService.LogConnectionTestAsync(
                $"{baseUrl.TrimEnd('/')}/authentication?tokenConsumerURL=test",
                "GET",
                new Dictionary<string, string>(),
                null,
                null,
                null,
                null,
                false,
                result.ErrorMessage,
                result.DurationMs,
                null);

            return result;
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            result.DurationMs = (int)stopwatch.ElapsedMilliseconds;
            result.Success = false;
            result.ErrorMessage = $"Connection timeout ({timeoutSeconds} seconds exceeded)";

            _logger.LogWarning("Griffin connection test timed out after {Timeout} seconds for {BaseUrl}", timeoutSeconds, baseUrl);

            // Log the timeout
            _ = _griffinApiLogService.LogConnectionTestAsync(
                $"{baseUrl.TrimEnd('/')}/authentication?tokenConsumerURL=test",
                "GET",
                new Dictionary<string, string>(),
                null,
                null,
                null,
                null,
                false,
                result.ErrorMessage,
                result.DurationMs,
                null);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.DurationMs = (int)stopwatch.ElapsedMilliseconds;
            result.Success = false;
            result.ErrorMessage = $"Unexpected error: {ex.Message}";

            _logger.LogError(ex, "Griffin connection test failed with unexpected error for {BaseUrl}", baseUrl);

            // Log the failure
            _ = _griffinApiLogService.LogConnectionTestAsync(
                $"{baseUrl.TrimEnd('/')}/authentication?tokenConsumerURL=test",
                "GET",
                new Dictionary<string, string>(),
                null,
                null,
                null,
                null,
                false,
                result.ErrorMessage,
                result.DurationMs,
                null);

            return result;
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
