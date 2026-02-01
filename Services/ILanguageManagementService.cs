using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Service for managing company language settings (default + alternate culture).
/// </summary>
public interface ILanguageManagementService
{
    /// <summary>
    /// Get language settings for a company.
    /// Returns defaults (en-US default, he-IL alternate) if not configured.
    /// </summary>
    Task<CompanyLanguageSettings> GetLanguageSettingsAsync(int companyId);

    /// <summary>
    /// Save language settings for a company.
    /// Creates new settings if they don't exist, updates if they do.
    /// </summary>
    Task<CompanyLanguageSettings> SaveLanguageSettingsAsync(int companyId, string defaultCulture, string alternateCulture, int userId);

    /// <summary>
    /// Validate language settings.
    /// Returns true if valid, false if invalid (with error message).
    /// </summary>
    bool ValidateLanguageSettings(string defaultCulture, string alternateCulture, out string? error);
}
