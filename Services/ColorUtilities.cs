using System.Text.RegularExpressions;

namespace ShiftManager.Services;

/// <summary>
/// Shared color-handling utilities. Extracted 2026-04-15 from duplicate private helpers
/// in ChoreTypes and DutyTypes admin pages; the AreaCalendarPalette page also needs this.
/// </summary>
public static class ColorUtilities
{
    private static readonly Regex HexColorRegex = new(@"^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    /// <summary>
    /// Accepts a user-supplied color string and returns it only if it matches strict
    /// <c>#RRGGBB</c>. Prevents CSS injection in <c>style=""</c> attributes.
    /// Returns <c>null</c> for null/empty/invalid input.
    /// </summary>
    public static string? SanitizeHexColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return null;
        var trimmed = color.Trim();
        return HexColorRegex.IsMatch(trimmed) ? trimmed : null;
    }
}
