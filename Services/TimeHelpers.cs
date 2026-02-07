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
        // Monday as start
        int delta = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-delta);
    }
}
