using System.Globalization;
using System.Text;

namespace ShiftManager.Services.Notifications;

/// <summary>
/// One event in the subscription feed. <see cref="StartUtc"/>/<see cref="EndUtc"/> are UTC for
/// timed events; for all-day events only the date component is used and the end is treated as
/// inclusive (the builder emits the iCal-exclusive DTEND by adding one day).
/// </summary>
public sealed record IcsEvent(
    string Uid,
    string Summary,
    DateTime StartUtc,
    DateTime EndUtc,
    bool IsAllDay,
    string? Location = null,
    string? Description = null);

/// <summary>
/// Pure RFC-5545 iCalendar generator for the self-hosted subscription feed (Phase 4). Stable per
/// assignment <c>UID</c>s mean updates/cancellations reconcile in the client instead of duplicating.
/// No I/O → fully unit-testable.
/// </summary>
public static class IcsBuilder
{
    private const string Crlf = "\r\n";

    public static string Build(IEnumerable<IcsEvent> events, string calendarName, DateTime nowUtc)
    {
        var stamp = nowUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.Append("BEGIN:VCALENDAR").Append(Crlf);
        sb.Append("VERSION:2.0").Append(Crlf);
        sb.Append("PRODID:-//ShiftManager//Calendar Feed//EN").Append(Crlf);
        sb.Append("CALSCALE:GREGORIAN").Append(Crlf);
        sb.Append("METHOD:PUBLISH").Append(Crlf);
        sb.Append("X-WR-CALNAME:").Append(Escape(calendarName)).Append(Crlf);

        foreach (var e in events)
        {
            sb.Append("BEGIN:VEVENT").Append(Crlf);
            sb.Append("UID:").Append(Escape(e.Uid)).Append(Crlf);
            sb.Append("DTSTAMP:").Append(stamp).Append(Crlf);

            if (e.IsAllDay)
            {
                sb.Append("DTSTART;VALUE=DATE:").Append(e.StartUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture)).Append(Crlf);
                // iCal all-day DTEND is exclusive → add one day to the inclusive end date.
                sb.Append("DTEND;VALUE=DATE:").Append(e.EndUtc.AddDays(1).ToString("yyyyMMdd", CultureInfo.InvariantCulture)).Append(Crlf);
            }
            else
            {
                sb.Append("DTSTART:").Append(e.StartUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)).Append(Crlf);
                sb.Append("DTEND:").Append(e.EndUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)).Append(Crlf);
            }

            sb.Append("SUMMARY:").Append(Escape(e.Summary)).Append(Crlf);
            if (!string.IsNullOrWhiteSpace(e.Location))
                sb.Append("LOCATION:").Append(Escape(e.Location)).Append(Crlf);
            if (!string.IsNullOrWhiteSpace(e.Description))
                sb.Append("DESCRIPTION:").Append(Escape(e.Description)).Append(Crlf);

            sb.Append("END:VEVENT").Append(Crlf);
        }

        sb.Append("END:VCALENDAR").Append(Crlf);
        return sb.ToString();
    }

    /// <summary>RFC-5545 TEXT escaping: backslash, semicolon, comma, and newlines.</summary>
    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value
            .Replace("\\", "\\\\")
            .Replace(";", "\\;")
            .Replace(",", "\\,")
            .Replace("\r\n", "\\n")
            .Replace("\n", "\\n")
            .Replace("\r", "\\n");
    }
}
