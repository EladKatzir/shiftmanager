using System.Text.RegularExpressions;

namespace ShiftManager.Helpers;

/// <summary>
/// Shared XSS/injection content validation for user-supplied strings.
/// </summary>
public static class InputSanitizer
{
    private static readonly string[] DangerousPatterns =
    {
        @"<script[^>]*>",
        @"</script>",
        @"javascript:",
        @"on\w+\s*=",       // onclick, onerror, onload, etc.
        @"<iframe[^>]*>",
        @"<object[^>]*>",
        @"<embed[^>]*>",
        @"<form[^>]*>",
        @"<input[^>]*>",
        @"<img[^>]*>",
        @"<link[^>]*>",
        @"<style[^>]*>",
        @"eval\s*\(",
        @"expression\s*\(",
    };

    /// <summary>
    /// Returns true if the input contains patterns commonly associated with XSS or injection attacks.
    /// </summary>
    public static bool ContainsDangerousContent(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;

        foreach (var pattern in DangerousPatterns)
        {
            if (Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase))
                return true;
        }

        return false;
    }
}
