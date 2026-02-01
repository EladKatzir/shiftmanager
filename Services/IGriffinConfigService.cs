using ShiftManager.Models;
using ShiftManager.Models.Support;

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
    /// Load Griffin config for specific company
    /// </summary>
    Task<GriffinConfig?> GetGriffinConfigByCompanyIdAsync(int companyId);

    /// <summary>
    /// Save Griffin configuration with audit logging
    /// </summary>
    Task<GriffinConfig> SaveGriffinConfigAsync(
        bool enabled,
        string? baseUrl,
        string? tokenConsumerUrl,
        bool autoProvisionUsers,
        UserRole defaultProvisionedRole,
        int timeoutSeconds,
        string updatedBy);

    /// <summary>
    /// Test if Griffin service is reachable with detailed diagnostics
    /// </summary>
    Task<GriffinConnectionTestResult> TestConnectionAsync(string baseUrl, int timeoutSeconds);
}
