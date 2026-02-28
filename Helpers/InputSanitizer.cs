using System.Text.RegularExpressions;

namespace ShiftManager.Helpers;

/// <summary>
/// Shared XSS/injection content validation for user-supplied strings.
/// </summary>
public static class InputSanitizer
{
    private static readonly Regex[] DangerousPatterns =
    {
        new(@"<script[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"</script>", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"javascript:", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"on\w+\s*=", RegexOptions.IgnoreCase | RegexOptions.Compiled),       // onclick, onerror, onload, etc.
        new(@"<iframe[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"<object[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"<embed[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"<form[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"<input[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"<img[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"<link[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"<style[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"eval\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"expression\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    };

    /// <summary>
    /// Returns true if the input contains patterns commonly associated with XSS or injection attacks.
    /// </summary>
    public static bool ContainsDangerousContent(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;

        foreach (var regex in DangerousPatterns)
        {
            if (regex.IsMatch(input))
                return true;
        }

        return false;
    }
}
