using System;
using FluentAssertions;
using ShiftManager.Services.Notifications;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// ICS-01 timed VEVENT shape (UTC DTSTART/DTEND, summary, location); ICS-02 all-day uses
/// VALUE=DATE with exclusive +1 end; ICS-03 RFC-5545 TEXT escaping.
/// </summary>
public class IcsBuilderTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact] // ICS-01
    public void Build_TimedEvent_EmitsVevent()
    {
        var ics = IcsBuilder.Build(new[]
        {
            new IcsEvent("shift-1", "Morning Shift",
                new DateTime(2026, 8, 8, 7, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 8, 8, 13, 0, 0, DateTimeKind.Utc),
                IsAllDay: false, Location: "HQ")
        }, "My Shifts", Now);

        ics.Should().StartWith("BEGIN:VCALENDAR\r\n");
        ics.Should().Contain("END:VCALENDAR");
        ics.Should().Contain("UID:shift-1");
        ics.Should().Contain("DTSTART:20260808T070000Z");
        ics.Should().Contain("DTEND:20260808T130000Z");
        ics.Should().Contain("SUMMARY:Morning Shift");
        ics.Should().Contain("LOCATION:HQ");
        ics.Should().Contain("DTSTAMP:20260601T120000Z");
    }

    [Fact] // ICS-02
    public void Build_AllDay_UsesExclusiveEnd()
    {
        var ics = IcsBuilder.Build(new[]
        {
            new IcsEvent("chore-9", "Kitchen",
                new DateTime(2026, 8, 8, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 8, 8, 0, 0, 0, DateTimeKind.Utc),
                IsAllDay: true)
        }, "Cal", Now);

        ics.Should().Contain("DTSTART;VALUE=DATE:20260808");
        ics.Should().Contain("DTEND;VALUE=DATE:20260809"); // inclusive end +1 day
    }

    [Fact] // ICS-03
    public void Build_EscapesSpecialChars()
    {
        var ics = IcsBuilder.Build(new[]
        {
            new IcsEvent("x", "A, B; C",
                new DateTime(2026, 8, 8, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 8, 8, 1, 0, 0, DateTimeKind.Utc),
                IsAllDay: false)
        }, "Cal", Now);

        ics.Should().Contain("SUMMARY:A\\, B\\; C");
    }
}
