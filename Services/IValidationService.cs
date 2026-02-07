namespace ShiftManager.Services;

/// <summary>
/// Service for validating user input with proper regex patterns and security checks.
/// </summary>
public interface IValidationService
{
    /// <summary>
    /// Validates an email address using a proper regex pattern.
    /// </summary>
    /// <param name="email">The email address to validate</param>
    /// <returns>True if valid, false otherwise</returns>
    bool IsValidEmail(string? email);

    /// <summary>
    /// Validates a phone number (supports international formats).
    /// </summary>
    /// <param name="phone">The phone number to validate</param>
    /// <returns>True if valid, false otherwise</returns>
    bool IsValidPhone(string? phone);

    /// <summary>
    /// Validates that a string contains only safe characters (alphanumeric + common punctuation).
    /// Prevents injection attacks and malformed data.
    /// </summary>
    /// <param name="input">The string to validate</param>
    /// <returns>True if safe, false otherwise</returns>
    bool IsSafeString(string? input);

    /// <summary>
    /// Normalizes input text for safe comparison and search.
    /// Applies Unicode NFC normalization to handle Hebrew niqqud consistently.
    /// </summary>
    /// <param name="input">The string to normalize</param>
    /// <returns>NFC-normalized, trimmed string</returns>
    string NormalizeForSearch(string? input);

    /// <summary>
    /// Validates a URL format.
    /// </summary>
    /// <param name="url">The URL to validate</param>
    /// <returns>True if valid, false otherwise</returns>
    bool IsValidUrl(string? url);
}
