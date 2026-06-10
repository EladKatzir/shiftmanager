using System;
using System.Text.Json;
using FluentAssertions;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Locks the Felix send routing + payload shape (MailService.BuildSendTarget) against the swagger:
/// CST-01 plain → base /mail/send url + flat mail; CST-02 timed → /calendar + wrapped calendarEvent
/// with UTC ISO times; CST-03 all-day → /allDayEvent + calendarAllDayEvent with date-only.
/// </summary>
public class CalendarSendTargetTests
{
    private static string Json(object o) =>
        JsonSerializer.Serialize(o, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    [Fact] // CST-01
    public void None_UsesBaseUrl_FlatPayload()
    {
        var (url, payload) = MailService.BuildSendTarget(
            "https://api.x/v1/mail/send", "f@x", "t@x", "Subj", "<p>h</p>",
            CalendarEventKind.None, null, null, null);

        url.Should().Be("https://api.x/v1/mail/send");
        var json = Json(payload);
        json.Should().Contain("\"from\":\"f@x\"").And.Contain("\"to\":\"t@x\"").And.Contain("\"html\":\"\\u003Cp\\u003Eh\\u003C/p\\u003E\"");
        json.Should().NotContain("calendarEvent");
    }

    [Fact] // CST-02
    public void Timed_RoutesToCalendar_WrapsEventInUtc()
    {
        var start = new DateTime(2026, 8, 8, 10, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 8, 8, 16, 0, 0, DateTimeKind.Utc);

        var (url, payload) = MailService.BuildSendTarget(
            "https://api.x/v1/mail/send/", "f@x", "t@x", "Subj", "h",
            CalendarEventKind.Timed, start, end, "HQ");

        url.Should().Be("https://api.x/v1/mail/send/calendar");
        var json = Json(payload);
        json.Should().Contain("\"mail\"");
        json.Should().Contain("\"calendarEvent\"");
        json.Should().Contain("\"startTime\":\"2026-08-08T10:00:00Z\"");
        json.Should().Contain("\"endTime\":\"2026-08-08T16:00:00Z\"");
        json.Should().Contain("\"location\":\"HQ\"");
    }

    [Fact] // CST-03
    public void AllDay_RoutesToAllDayEvent_DateOnly()
    {
        var start = new DateTime(2026, 8, 8, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);

        var (url, payload) = MailService.BuildSendTarget(
            "https://api.x/v1/mail/send", "f@x", "t@x", "Subj", "h",
            CalendarEventKind.AllDay, start, end, "");

        url.Should().Be("https://api.x/v1/mail/send/allDayEvent");
        var json = Json(payload);
        json.Should().Contain("\"calendarAllDayEvent\"");
        json.Should().Contain("\"startTime\":\"2026-08-08\"");
        json.Should().Contain("\"endTime\":\"2026-08-10\"");
    }
}
