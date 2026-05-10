using ShiftManager.Models;
using ShiftManager.Models.Support;
// UserRole no longer referenced — provisioning role is no longer stored in GriffinConfig.

namespace ShiftManager.Services;

/// <summary>
/// Service for managing Griffin ADFS configuration
/// </summary>
public interface IGriffinConfigService
{
    /// <summary>
    /// Load Griffin config for current company (via ITenantResolver)
    /// </summary>
    Task<GriffinConfig?> GetGriffinConfigAsync();

    /// <summary>
    /// Load any enabled Griffin config from the database (ignoring tenant context).
    /// Used on the login page where no tenant context exists yet.
    /// </summary>
    Task<GriffinConfig?> GetAnyEnabledGriffinConfigAsync();

    /// <summary>
    /// Load Griffin config for specific company
    /// </summary>
    Task<GriffinConfig?> GetGriffinConfigByCompanyIdAsync(int companyId);

    /// <summary>
    /// Save Griffin configuration with audit logging.
    /// User-provisioning behaviour is controlled by the FF_ALLOW_USERS_CREATION_VIA_ADFS
    /// feature flag, not by this service.
    /// </summary>
    Task<GriffinConfig> SaveGriffinConfigAsync(
        bool enabled,
        string? baseUrl,
        string? tokenConsumerUrl,
        int timeoutSeconds,
        string updatedBy);

    /// <summary>
    /// Test if Griffin service is reachable with detailed diagnostics
    /// </summary>
    Task<GriffinConnectionTestResult> TestConnectionAsync(string baseUrl, int timeoutSeconds);
}
