using System.Globalization;

namespace ShiftManager.Services;

/// <summary>
/// Single, shared formatter for chore/shift durations in minutes. Bilingual via
/// <see cref="CultureInfo.CurrentUICulture"/> (Hebrew = he/he-IL). Used by admin weight inputs, the
/// chores calendar, and the Justice/Analytics fairness display. Defined ONCE here so the unit symbols
/// never drift across phases.
/// </summary>
public static class DurationFormat
{
    private static bool IsHebrew
        => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("he", StringComparison.OrdinalIgnoreCase);

    /// <summary>"1h 30m" / "1ש׳ 30ד׳". Whole hours drop the minutes; sub-hour drops the hours; 0 → "0m"/"0ד׳".</summary>
    public static string FormatMinutes(int totalMinutes)
    {
        if (totalMinutes < 0) totalMinutes = 0;
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;

        var (h, m) = IsHebrew ? ("ש׳", "ד׳") : ("h", "m");

        if (hours > 0 && minutes > 0)
            return $"{hours}{h} {minutes}{m}";
        if (hours > 0)
            return $"{hours}{h}";
        return $"{minutes}{m}";
    }

    /// <summary>"12.5h" / "12.5ש׳". Trailing ".0" is dropped (12h, not 12.0h). Invariant decimal point.</summary>
    public static string FormatHours(int totalMinutes)
    {
        if (totalMinutes < 0) totalMinutes = 0;
        var hours = totalMinutes / 60.0;
        // Round to 1 decimal; invariant '.' so the symbol reads the same in both languages.
        var text = hours.ToString("0.#", CultureInfo.InvariantCulture);
        return IsHebrew ? $"{text}ש׳" : $"{text}h";
    }
}
