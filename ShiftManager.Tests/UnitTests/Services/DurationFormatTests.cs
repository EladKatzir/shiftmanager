using System.Globalization;
using FluentAssertions;
using ShiftManager.Services;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Pure formatting coverage for <see cref="DurationFormat"/>. Forces CurrentUICulture per-case so the
/// bilingual branch (en vs he) is deterministic regardless of the test host's locale.
/// </summary>
public class DurationFormatTests
{
    private static T WithCulture<T>(string culture, Func<T> f)
    {
        var prev = CultureInfo.CurrentUICulture;
        try { CultureInfo.CurrentUICulture = new CultureInfo(culture); return f(); }
        finally { CultureInfo.CurrentUICulture = prev; }
    }

    [Theory]
    [InlineData(90, "1h 30m")]
    [InlineData(60, "1h")]
    [InlineData(45, "45m")]
    [InlineData(0, "0m")]
    [InlineData(480, "8h")]
    [InlineData(125, "2h 5m")]
    public void FormatMinutes_English(int minutes, string expected)
        => WithCulture("en-US", () => DurationFormat.FormatMinutes(minutes)).Should().Be(expected);

    [Theory]
    [InlineData(90, "1ש׳ 30ד׳")]
    [InlineData(60, "1ש׳")]
    [InlineData(45, "45ד׳")]
    [InlineData(0, "0ד׳")]
    public void FormatMinutes_Hebrew(int minutes, string expected)
        => WithCulture("he-IL", () => DurationFormat.FormatMinutes(minutes)).Should().Be(expected);

    [Theory]
    [InlineData(750, "12.5h")]
    [InlineData(480, "8h")]
    [InlineData(720, "12h")]
    [InlineData(90, "1.5h")]
    public void FormatHours_English(int minutes, string expected)
        => WithCulture("en-US", () => DurationFormat.FormatHours(minutes)).Should().Be(expected);

    [Theory]
    [InlineData(750, "12.5ש׳")]
    [InlineData(480, "8ש׳")]
    public void FormatHours_Hebrew(int minutes, string expected)
        => WithCulture("he-IL", () => DurationFormat.FormatHours(minutes)).Should().Be(expected);
}
