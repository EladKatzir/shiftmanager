namespace ShiftManager.Helpers;

/// <summary>
/// Converts ShiftManager's local (Israel) wall-clock shift times to UTC for calendar artifacts
/// (Felix calendar invites and the .ics subscription feed). Shared so the mail layer and the feed
/// layer agree on the conversion. IANA id works on .NET 8 (ICU) across Windows/Linux, with the
/// Windows id + UTC as fallbacks.
/// </summary>
public static class IsraelTime
{
    private static readonly TimeZoneInfo Tz = Resolve();

    private static TimeZoneInfo Resolve()
    {
        foreach (var id in new[] { "Asia/Jerusalem", "Israel Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }

    /// <summary>Convert a local (Israel) date+time to UTC.</summary>
    public static DateTime ToUtc(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, Tz);
    }

    /// <summary>
    /// UTC start/end for a shift, accounting for overnight shifts (end ≤ start rolls to next day).
    /// </summary>
    public static (DateTime startUtc, DateTime endUtc) ShiftWindowUtc(DateOnly date, TimeOnly start, TimeOnly end)
    {
        var startUtc = ToUtc(date, start);
        var endDate = end > start ? date : date.AddDays(1);
        return (startUtc, ToUtc(endDate, end));
    }
}
