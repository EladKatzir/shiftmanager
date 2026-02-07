using System.Text;
using System.Text.RegularExpressions;

namespace ShiftManager.Services;

/// <summary>
/// Implementation of input validation service with regex patterns and security checks.
/// </summary>
public class ValidationService : IValidationService
{
    // Email regex pattern (RFC 5322 simplified)
    // Validates most common email formats while rejecting obviously invalid inputs
    private static readonly Regex EmailRegex = new Regex(
        @"^[a-zA-Z0-9.!#$%&'*+\/=?^_`{|}~-]+@[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100)); // Regex timeout to prevent ReDoS attacks

    // Phone regex pattern (supports international formats)
    // Allows: +1-234-567-8900, (123) 456-7890, 123-456-7890, 1234567890, +44 20 1234 5678
    private static readonly Regex PhoneRegex = new Regex(
        @"^[\+]?[(]?[0-9]{1,4}[)]?[-\s\.]?[(]?[0-9]{1,4}[)]?[-\s\.]?[0-9]{1,9}[-\s\.]?[0-9]{0,9}$",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    // Safe string pattern (alphanumeric + common safe punctuation)
    // Prevents most injection attacks while allowing normal text with punctuation
    private static readonly Regex SafeStringRegex = new Regex(
        @"^[a-zA-Z0-9\s\.,;:!?'""\-_@#$%&()\/\[\]]*$",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    // URL regex pattern (http/https URLs)
    private static readonly Regex UrlRegex = new Regex(
        @"^https?:\/\/(?:www\.)?[-a-zA-Z0-9@:%._\+~#=]{1,256}\.[a-zA-Z0-9()]{1,6}\b(?:[-a-zA-Z0-9()@:%_\+.~#?&\/=]*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    public bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        // Length check (RFC 5321: max 254 chars for email address)
        if (email.Length > 254)
            return false;

        try
        {
            return EmailRegex.IsMatch(email);
        }
        catch (RegexMatchTimeoutException)
        {
            // Regex timeout - likely a ReDoS attack attempt
            return false;
        }
    }

    public bool IsValidPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return false;

        // Length check (reasonable phone number length)
        if (phone.Length < 7 || phone.Length > 20)
            return false;

        try
        {
            return PhoneRegex.IsMatch(phone);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    public bool IsSafeString(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return true; // Empty strings are safe

        try
        {
            return SafeStringRegex.IsMatch(input);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// Normalizes input text for safe comparison and search.
    /// Applies Unicode NFC normalization to handle Hebrew niqqud consistently (fixes A-13).
    /// </summary>
    public string NormalizeForSearch(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        // NFC normalization ensures consistent representation of Hebrew characters with niqqud
        return input.Normalize(NormalizationForm.FormC).Trim();
    }

    public bool IsValidUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        // Length check
        if (url.Length > 2048)
            return false;

        try
        {
            return UrlRegex.IsMatch(url);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
