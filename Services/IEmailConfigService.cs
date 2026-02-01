using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Service for managing email configuration settings
/// </summary>
public interface IEmailConfigService
{
    /// <summary>
    /// Gets the email configuration for the current company
    /// </summary>
    /// <returns>The email configuration, or null if not configured</returns>
    Task<EmailConfig?> GetEmailConfigAsync();

    /// <summary>
    /// Gets the email configuration for a specific company (requires ignoring query filters)
    /// </summary>
    /// <param name="companyId">The company ID</param>
    /// <returns>The email configuration, or null if not configured</returns>
    Task<EmailConfig?> GetEmailConfigByCompanyIdAsync(int companyId);

    /// <summary>
    /// Saves or updates email configuration for the current company
    /// </summary>
    /// <param name="enabled">Whether email is enabled</param>
    /// <param name="apiKey">The API key (will be encrypted)</param>
    /// <param name="apiUrl">The API URL</param>
    /// <param name="fromAddress">The from email address</param>
    /// <param name="updatedBy">User who made the update</param>
    /// <returns>The saved configuration</returns>
    Task<EmailConfig> SaveEmailConfigAsync(bool enabled, string? apiKey, string? apiUrl, string? fromAddress, string updatedBy);

    /// <summary>
    /// Gets the decrypted API key for the current company
    /// </summary>
    /// <returns>The decrypted API key, or null if not set</returns>
    Task<string?> GetDecryptedApiKeyAsync();
}
