using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

public partial class GriffinConfigService : IGriffinConfigService
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
            if (companyId > 0)
            {
                var config = await GetGriffinConfigByCompanyIdAsync(companyId);
                if (config != null) return config;
            }
        }
        catch (Exception ex)
        {
            LogTenantResolverError(_logger, ex);
        }

        // Fallback for unauthenticated users (login page, callback) or when tenant-scoped lookup found nothing
        var anyConfig = await GetAnyEnabledGriffinConfigAsync();
        if (anyConfig != null)
        {
            LogConfigFoundWithoutTenant(_logger, anyConfig.CompanyId);
            return anyConfig;
        }

        return GetConfigFromAppSettings();
    }

    public async Task<GriffinConfig?> GetAnyEnabledGriffinConfigAsync()
    {
        try
        {
            // SECURITY-AUDITED: IgnoreQueryFilters needed — called from login page before tenant context exists;
            // returns the most recently updated enabled config to ensure admin changes take effect
            var config = await _dbContext.GriffinConfigs
                .IgnoreQueryFilters()
                .Where(c => c.Enabled && !string.IsNullOrEmpty(c.BaseUrl) && !string.IsNullOrEmpty(c.TokenConsumerUrl))
                .OrderByDescending(c => c.LastUpdated)
                .FirstOrDefaultAsync();

            if (config != null)
            {
                LogConfigFoundInDatabase(_logger, config.CompanyId, config.BaseUrl);
            }
            else
            {
                LogNoConfigInDatabase(_logger);
            }

            return config;
        }
        catch (Exception ex)
        {
            LogAnyConfigQueryError(_logger, ex);
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
            LogDbConfigHeader(_logger, companyId);
            LogDbConfigEnabled(_logger, dbConfig.Enabled);
            LogDbConfigBaseUrl(_logger, dbConfig.BaseUrl ?? "(null)");
            LogDbConfigTokenConsumerUrl(_logger, dbConfig.TokenConsumerUrl ?? "(null)");
            LogDbConfigTimeout(_logger, dbConfig.TimeoutSeconds);
            return dbConfig;
        }

        // Fallback to appsettings
        LogUsingAppSettingsFallback(_logger, companyId);
        var fallbackConfig = GetConfigFromAppSettings();

        if (fallbackConfig != null)
        {
            fallbackConfig.CompanyId = companyId;
            LogFallbackConfigHeader(_logger);
            LogFallbackEnabled(_logger, fallbackConfig.Enabled);
            LogFallbackBaseUrl(_logger, fallbackConfig.BaseUrl ?? "(null)");
            LogFallbackTokenConsumerUrl(_logger, fallbackConfig.TokenConsumerUrl ?? "(null)");
        }
        else
        {
            LogNoFallbackConfig(_logger);
        }

        return fallbackConfig;
    }

    public async Task<GriffinConfig> SaveGriffinConfigAsync(
        bool enabled,
        string? baseUrl,
        string? tokenConsumerUrl,
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
                TimeoutSeconds = timeoutSeconds,
                LastUpdated = DateTime.UtcNow,
                LastUpdatedBy = updatedBy
            };

            _dbContext.GriffinConfigs.Add(config);
            LogSaveHeader(_logger, "Created new", companyId);
            LogSaveEnabled(_logger, enabled);
            LogSaveBaseUrl(_logger, baseUrl ?? "(null)");
            LogSaveTokenConsumerUrl(_logger, tokenConsumerUrl ?? "(null)");
            LogSaveUpdatedBy(_logger, updatedBy);
        }
        else
        {
            // Update existing
            config.Enabled = enabled;
            config.BaseUrl = baseUrl;
            config.TokenConsumerUrl = tokenConsumerUrl;
            config.TimeoutSeconds = timeoutSeconds;
            config.LastUpdated = DateTime.UtcNow;
            config.LastUpdatedBy = updatedBy;

            LogSaveHeader(_logger, "Updated", companyId);
            LogSaveEnabled(_logger, enabled);
            LogSaveBaseUrl(_logger, baseUrl ?? "(null)");
            LogSaveTokenConsumerUrl(_logger, tokenConsumerUrl ?? "(null)");
            LogSaveUpdatedBy(_logger, updatedBy);
        }

        await _dbContext.SaveChangesAsync();

        LogSaveSuccess(_logger, companyId);

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
            using var client = _httpClientFactory.CreateClient("GriffinClient");
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            var testUrl = $"{baseUrl.TrimEnd('/')}/authentication?tokenConsumerURL=test";
            var requestHeaders = new Dictionary<string, string>();

            LogTestConnectionStart(_logger, testUrl);

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

            LogTestConnectionCompleted(_logger, isSuccess ? "SUCCESS" : "FAILURE", result.StatusCode, result.DurationMs);

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

            LogTestConnectionNetworkError(_logger, ex, baseUrl);

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

            LogTestConnectionTimeout(_logger, timeoutSeconds, baseUrl);

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

            LogTestConnectionUnexpectedError(_logger, ex, baseUrl);

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

        var timeoutSeconds = _configuration.GetValue<int>("Griffin:TimeoutSeconds", 30);

        return new GriffinConfig
        {
            Enabled = enabled,
            BaseUrl = baseUrl,
            TokenConsumerUrl = tokenConsumerUrl,
            TimeoutSeconds = timeoutSeconds
        };
    }
}
