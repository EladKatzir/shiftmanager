using Microsoft.Extensions.Logging;
// UserRole no longer logged — provisioning role moved to FF_ALLOW_USERS_CREATION_VIA_ADFS feature flag.

namespace ShiftManager.Services;

// Source-generated LoggerMessage delegates for GriffinConfigService (closes 38 sites of CA1848).
// EventId range 7000-7099 reserved (using 7000-7044). 24 unique methods cover 38 call sites —
// Save header line (Created/Updated) collapsed via a shared Pattern-C method that takes an
// Action verb. The Save per-field block (Enabled/BaseUrl/TokenConsumerUrl/AutoProvision/Role/
// UpdatedBy) is reused twice (Created and Updated branches), saving 6 extra methods.
// Range allocations:
//   7000-7009: GetGriffinConfigAsync + GetAnyEnabledGriffinConfigAsync (5 sites, 5 methods)
//   7010-7019: GetGriffinConfigByCompanyIdAsync — db config dump (7 sites, 7 methods)
//   7020-7029: GetGriffinConfigByCompanyIdAsync — appsettings fallback dump (6 sites, 6 methods)
//   7030-7039: SaveGriffinConfigAsync — header + per-field dump + saved confirmation
//             (15 sites collapsed to 8 methods via shared header verb + reused per-field methods)
//   7040-7049: TestConnectionAsync (5 sites, 5 methods)
public partial class GriffinConfigService
{
    // ── GetGriffinConfigAsync + GetAnyEnabledGriffinConfigAsync ────────────

    [LoggerMessage(EventId = 7000, Level = LogLevel.Debug,
        Message = "Unable to get Griffin config via tenant resolver")]
    private static partial void LogTenantResolverError(ILogger logger, System.Exception ex);

    [LoggerMessage(EventId = 7001, Level = LogLevel.Debug,
        Message = "Found enabled Griffin config from database (CompanyId={CompanyId}) without tenant context")]
    private static partial void LogConfigFoundWithoutTenant(ILogger logger, int companyId);

    [LoggerMessage(EventId = 7002, Level = LogLevel.Debug,
        Message = "Found enabled Griffin config in database (CompanyId={CompanyId}, BaseUrl={BaseUrl})")]
    private static partial void LogConfigFoundInDatabase(ILogger logger, int companyId, string? baseUrl);

    [LoggerMessage(EventId = 7003, Level = LogLevel.Debug,
        Message = "No enabled Griffin config found in database")]
    private static partial void LogNoConfigInDatabase(ILogger logger);

    [LoggerMessage(EventId = 7004, Level = LogLevel.Debug,
        Message = "Error querying database for any enabled Griffin config")]
    private static partial void LogAnyConfigQueryError(ILogger logger, System.Exception ex);

    // ── GetGriffinConfigByCompanyIdAsync — db config dump ──────────────────

    [LoggerMessage(EventId = 7010, Level = LogLevel.Debug,
        Message = "Loaded Griffin config from database for company {CompanyId}:")]
    private static partial void LogDbConfigHeader(ILogger logger, int companyId);

    [LoggerMessage(EventId = 7011, Level = LogLevel.Debug,
        Message = "  - Enabled: {Enabled}")]
    private static partial void LogDbConfigEnabled(ILogger logger, bool enabled);

    [LoggerMessage(EventId = 7012, Level = LogLevel.Debug,
        Message = "  - BaseUrl: {BaseUrl}")]
    private static partial void LogDbConfigBaseUrl(ILogger logger, string baseUrl);

    [LoggerMessage(EventId = 7013, Level = LogLevel.Debug,
        Message = "  - TokenConsumerUrl: {TokenConsumerUrl}")]
    private static partial void LogDbConfigTokenConsumerUrl(ILogger logger, string tokenConsumerUrl);

    [LoggerMessage(EventId = 7016, Level = LogLevel.Debug,
        Message = "  - TimeoutSeconds: {Timeout}")]
    private static partial void LogDbConfigTimeout(ILogger logger, int timeout);

    // ── GetGriffinConfigByCompanyIdAsync — appsettings fallback dump ───────

    [LoggerMessage(EventId = 7020, Level = LogLevel.Debug,
        Message = "No Griffin config in database for company {CompanyId}, using appsettings fallback")]
    private static partial void LogUsingAppSettingsFallback(ILogger logger, int companyId);

    [LoggerMessage(EventId = 7021, Level = LogLevel.Debug,
        Message = "Fallback config from appsettings.json:")]
    private static partial void LogFallbackConfigHeader(ILogger logger);

    [LoggerMessage(EventId = 7022, Level = LogLevel.Debug,
        Message = "  - Enabled: {Enabled}")]
    private static partial void LogFallbackEnabled(ILogger logger, bool enabled);

    [LoggerMessage(EventId = 7023, Level = LogLevel.Debug,
        Message = "  - BaseUrl: {BaseUrl}")]
    private static partial void LogFallbackBaseUrl(ILogger logger, string baseUrl);

    [LoggerMessage(EventId = 7024, Level = LogLevel.Debug,
        Message = "  - TokenConsumerUrl: {TokenConsumerUrl}")]
    private static partial void LogFallbackTokenConsumerUrl(ILogger logger, string tokenConsumerUrl);

    [LoggerMessage(EventId = 7025, Level = LogLevel.Debug,
        Message = "No fallback config available from appsettings.json (Enabled=false or BaseUrl missing)")]
    private static partial void LogNoFallbackConfig(ILogger logger);

    // ── SaveGriffinConfigAsync — header + per-field dump + saved confirmation ──

    [LoggerMessage(EventId = 7030, Level = LogLevel.Information,
        Message = "{Action} Griffin config for company {CompanyId}:")]
    private static partial void LogSaveHeader(ILogger logger, string action, int companyId);

    [LoggerMessage(EventId = 7031, Level = LogLevel.Information,
        Message = "  - Enabled: {Enabled}")]
    private static partial void LogSaveEnabled(ILogger logger, bool enabled);

    [LoggerMessage(EventId = 7032, Level = LogLevel.Information,
        Message = "  - BaseUrl: {BaseUrl}")]
    private static partial void LogSaveBaseUrl(ILogger logger, string baseUrl);

    [LoggerMessage(EventId = 7033, Level = LogLevel.Information,
        Message = "  - TokenConsumerUrl: {TokenConsumerUrl}")]
    private static partial void LogSaveTokenConsumerUrl(ILogger logger, string tokenConsumerUrl);

    [LoggerMessage(EventId = 7036, Level = LogLevel.Information,
        Message = "  - UpdatedBy: {User}")]
    private static partial void LogSaveUpdatedBy(ILogger logger, string user);

    [LoggerMessage(EventId = 7037, Level = LogLevel.Information,
        Message = "Griffin config saved successfully to database for company {CompanyId}")]
    private static partial void LogSaveSuccess(ILogger logger, int companyId);

    // ── TestConnectionAsync ────────────────────────────────────────────────

    [LoggerMessage(EventId = 7040, Level = LogLevel.Debug,
        Message = "Testing Griffin connection to: {TestUrl}")]
    private static partial void LogTestConnectionStart(ILogger logger, string testUrl);

    [LoggerMessage(EventId = 7041, Level = LogLevel.Information,
        Message = "Griffin connection test completed: {Success}, Status: {StatusCode}, Duration: {Duration}ms")]
    private static partial void LogTestConnectionCompleted(ILogger logger, string success, int? statusCode, int duration);

    [LoggerMessage(EventId = 7042, Level = LogLevel.Error,
        Message = "Griffin connection test failed: Network error for {BaseUrl}")]
    private static partial void LogTestConnectionNetworkError(ILogger logger, System.Exception ex, string baseUrl);

    [LoggerMessage(EventId = 7043, Level = LogLevel.Warning,
        Message = "Griffin connection test timed out after {Timeout} seconds for {BaseUrl}")]
    private static partial void LogTestConnectionTimeout(ILogger logger, int timeout, string baseUrl);

    [LoggerMessage(EventId = 7044, Level = LogLevel.Error,
        Message = "Griffin connection test failed with unexpected error for {BaseUrl}")]
    private static partial void LogTestConnectionUnexpectedError(ILogger logger, System.Exception ex, string baseUrl);
}
