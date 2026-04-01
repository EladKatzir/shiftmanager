using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Service for managing email configuration settings
/// </summary>
public interface IEmailConfigService
{
    /// <summary>
    /// Gets the effective email configuration for the current company.
    /// Fallback: company-specific → global → null.
    /// </summary>
    Task<EmailConfig?> GetEmailConfigAsync();

    /// <summary>
    /// Gets the global email configuration (CompanyId = null).
    /// </summary>
    Task<EmailConfig?> GetGlobalEmailConfigAsync();

    /// <summary>
    /// Gets the email configuration for a specific company (requires ignoring query filters)
    /// </summary>
    Task<EmailConfig?> GetEmailConfigByCompanyIdAsync(int companyId);

    /// <summary>
    /// Saves or updates email configuration for the current company
    /// </summary>
    Task<EmailConfig> SaveEmailConfigAsync(bool enabled, string? apiKey, string? apiUrl, string? fromAddress, string updatedBy);

    /// <summary>
    /// Saves or updates the global email configuration (CompanyId = null).
    /// </summary>
    Task<EmailConfig> SaveGlobalEmailConfigAsync(bool enabled, string? apiKey, string? apiUrl, string? fromAddress, string updatedBy);

    /// <summary>
    /// Deletes the company-specific override, causing it to fall back to global config.
    /// </summary>
    Task<bool> DeleteCompanyOverrideAsync();

    /// <summary>
    /// Checks whether the current company has a company-specific override (vs using global).
    /// </summary>
    Task<bool> HasCompanyOverrideAsync();

    /// <summary>
    /// Gets the decrypted API key for the effective config (company-specific or global).
    /// </summary>
    Task<string?> GetDecryptedApiKeyAsync();
}
