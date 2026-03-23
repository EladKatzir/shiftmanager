using ShiftManager.Models;

namespace ShiftManager.Services;

public static class TimeHelpers
{
    public static (DateTime start, DateTime end) GetShiftWindow(ShiftType t, DateOnly date)
    {
        // Use DateOnly.ToDateTime(TimeOnly) – not ToTimeSpan()
        var start = date.ToDateTime(t.Start);
        var end = date.ToDateTime(t.End);
        if (t.End <= t.Start)
        {
            // wraps past midnight
            end = end.AddDays(1);
        }
        return (start, end);
    }

    public static double Hours(ShiftType t)
    {
        return Hours(t, DateOnly.FromDateTime(DateTime.Today));
    }

    /// <summary>
    /// Returns DST-aware shift duration for a specific date.
    /// On DST transition dates (2 per year), duration may differ from the nominal value
    /// (e.g., a NIGHT shift spanning spring-forward loses 1 hour, fall-back gains 1 hour).
    /// </summary>
    public static double Hours(ShiftType t, DateOnly date)
    {
        var tz = TimeZoneInfo.Local;
        var startLocal = date.ToDateTime(t.Start);
        var endLocal = t.End <= t.Start
            ? date.AddDays(1).ToDateTime(t.End) // overnight shift
            : date.ToDateTime(t.End);

        // Convert to UTC to get true elapsed time (accounts for DST transitions)
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, tz);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, tz);

        return (endUtc - startUtc).TotalHours;
    }

    public static DateOnly WeekStart(DateOnly date)
    {
        // Monday as start (default)
        return WeekStart(date, DayOfWeek.Sunday);
    }

    /// <summary>
    /// A-05: Configurable week start day. IDF units may use Sunday as week start.
    /// </summary>
    public static DateOnly WeekStart(DateOnly date, DayOfWeek startDay)
    {
        int delta = ((int)date.DayOfWeek - (int)startDay + 7) % 7;
        return date.AddDays(-delta);
    }

    /// <summary>
    /// Merges overlapping time windows and sums total unique hours.
    /// Prevents double-counting when shifts overlap (e.g., two overlapping 8hr offline shifts
    /// should count as 8hr, not 16hr). Ported from ConflictChecker.
    /// </summary>
    public static double MergeAndSumHours(List<(DateTime start, DateTime end)> windows)
    {
        if (windows.Count == 0) return 0;

        double total = 0;
        var currentStart = windows[0].start;
        var currentEnd = windows[0].end;

        for (int i = 1; i < windows.Count; i++)
        {
            if (windows[i].start <= currentEnd)
            {
                // Overlapping — extend the merged window
                if (windows[i].end > currentEnd)
                    currentEnd = windows[i].end;
            }
            else
            {
                // No overlap — add the merged window and start a new one
                total += (currentEnd - currentStart).TotalHours;
                currentStart = windows[i].start;
                currentEnd = windows[i].end;
            }
        }

        total += (currentEnd - currentStart).TotalHours;
        return total;
    }
}
