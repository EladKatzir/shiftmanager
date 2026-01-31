using System.Security.Cryptography;
using System.Text;

namespace ShiftManager.Helpers;

/// <summary>
/// Helper class for masking and protecting PII (Personally Identifiable Information).
/// B-036: Sensitive Data Exposure Audit
///
/// Use these methods when displaying PII to users who should not see the full data:
/// - Non-owners viewing other users' contact information
/// - Analytics and logging contexts
/// - Error messages and stack traces
/// </summary>
public static class PrivacyHelper
{
    /// <summary>
    /// Masks a phone number showing only last 4 digits.
    /// Input: "123-456-7890" or "1234567890" or "050-1234567"
    /// Output: "***-***-7890" or "***-***-4567"
    ///
    /// Note: For emergency on-call contacts, full phone numbers are intentionally displayed
    /// as this is a core business requirement for the widget.
    /// </summary>
    /// <param name="phone">The phone number to mask</param>
    /// <returns>Masked phone number with only last 4 digits visible</returns>
    public static string MaskPhone(string? phone)
    {
        if (string.IsNullOrEmpty(phone)) return "";

        // Remove non-digits
        var digits = new string(phone.Where(char.IsDigit).ToArray());

        if (digits.Length < 4) return "***";

        var last4 = digits[^4..];
        return $"***-***-{last4}";
    }

    /// <summary>
    /// Masks an email showing only first character of local part and full domain.
    /// Input: "john.doe@example.com"
    /// Output: "j***@example.com"
    ///
    /// Useful when displaying emails to users who shouldn't see the full address,
    /// such as in public-facing assignment lists or audit logs.
    /// </summary>
    /// <param name="email">The email address to mask</param>
    /// <returns>Masked email with first character and domain visible</returns>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrEmpty(email)) return "";

        var atIndex = email.IndexOf('@');
        if (atIndex <= 0) return "***";

        return $"{email[0]}***{email[atIndex..]}";
    }

    /// <summary>
    /// Hashes a user ID for analytics purposes, providing a consistent but non-reversible identifier.
    /// This allows tracking user behavior patterns without exposing actual user IDs.
    ///
    /// The hash is salted with a project-specific prefix to prevent rainbow table attacks.
    /// </summary>
    /// <param name="userId">The user ID to hash</param>
    /// <returns>16-character lowercase hex string (first 64 bits of SHA-256)</returns>
    public static string HashUserId(int userId)
    {
        var bytes = Encoding.UTF8.GetBytes($"shifty_analytics:{userId}");
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    /// <summary>
    /// Hashes a string identifier for analytics purposes.
    /// Useful for hashing email addresses or usernames before sending to external analytics.
    /// </summary>
    /// <param name="identifier">The string identifier to hash</param>
    /// <returns>16-character lowercase hex string</returns>
    public static string HashIdentifier(string? identifier)
    {
        if (string.IsNullOrEmpty(identifier)) return "0000000000000000";

        var bytes = Encoding.UTF8.GetBytes($"shifty_analytics:{identifier}");
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    /// <summary>
    /// Scrubs PII from text content (for error messages, logs, etc.).
    /// Replaces emails with [EMAIL], phone numbers with [PHONE], and long tokens with [TOKEN].
    /// </summary>
    /// <param name="text">The text to scrub</param>
    /// <returns>Text with PII replaced by placeholders</returns>
    public static string ScrubPii(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";

        // Scrub email addresses
        text = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}",
            "[EMAIL]");

        // Scrub phone numbers (various formats)
        text = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"(\+?\d{1,3}[-.\s]?)?\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}",
            "[PHONE]");

        // Scrub potential tokens (long alphanumeric strings)
        text = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"[a-zA-Z0-9]{32,}",
            "[TOKEN]");

        return text;
    }

    /// <summary>
    /// Masks a name to show only initials.
    /// Input: "John Doe"
    /// Output: "J. D."
    /// </summary>
    /// <param name="name">The full name to mask</param>
    /// <returns>Initials with periods</returns>
    public static string MaskName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "";

        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "";

        return string.Join(" ", parts.Select(p => $"{p[0]}."));
    }

    /// <summary>
    /// Truncates a string and adds ellipsis if it exceeds the specified length.
    /// Useful for displaying preview text without revealing full content.
    /// </summary>
    /// <param name="text">The text to truncate</param>
    /// <param name="maxLength">Maximum length before truncation (default: 30)</param>
    /// <returns>Truncated text with ellipsis if needed</returns>
    public static string Truncate(string? text, int maxLength = 30)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength) return text ?? "";
        return text[..maxLength] + "...";
    }
}
