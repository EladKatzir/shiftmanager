using FluentAssertions;

namespace ShiftManager.Tests.UnitTests.Calendar;

/// <summary>
/// Unit tests for calendar pagination date validation.
/// Task A-019: Calendar Pagination Correctness
/// </summary>
public class CalendarDateValidationTests
{
    #region TryCreateValidDate Tests (Day/Week views)

    [Fact]
    public void TryCreateValidDate_ValidDate_ReturnsDate()
    {
        // Arrange
        int year = 2026, month = 1, day = 15;

        // Act
        var result = TryCreateValidDate(year, month, day);

        // Assert
        result.Should().NotBeNull();
        result!.Value.Year.Should().Be(2026);
        result.Value.Month.Should().Be(1);
        result.Value.Day.Should().Be(15);
    }

    [Fact]
    public void TryCreateValidDate_LeapYear_Feb29_ReturnsValidDate()
    {
        // Arrange - 2024 and 2028 are leap years
        int year = 2024, month = 2, day = 29;

        // Act
        var result = TryCreateValidDate(year, month, day);

        // Assert
        result.Should().NotBeNull();
        result!.Value.Year.Should().Be(2024);
        result.Value.Month.Should().Be(2);
        result.Value.Day.Should().Be(29);
    }

    [Fact]
    public void TryCreateValidDate_NonLeapYear_Feb29_ClampsTo28()
    {
        // Arrange - 2025 is not a leap year
        int year = 2025, month = 2, day = 29;

        // Act
        var result = TryCreateValidDate(year, month, day);

        // Assert
        result.Should().NotBeNull();
        result!.Value.Year.Should().Be(2025);
        result.Value.Month.Should().Be(2);
        result.Value.Day.Should().Be(28); // Clamped to max valid day
    }

    [Fact]
    public void TryCreateValidDate_January31_NavigateToFeb_ClampsTo28Or29()
    {
        // Arrange - Simulating navigation from Jan 31 to Feb (non-leap year)
        int year = 2025, month = 2, day = 31;

        // Act
        var result = TryCreateValidDate(year, month, day);

        // Assert
        result.Should().NotBeNull();
        result!.Value.Day.Should().Be(28); // Clamped to Feb's max day
    }

    [Fact]
    public void TryCreateValidDate_YearBoundary_December31_Works()
    {
        // Arrange
        int year = 2025, month = 12, day = 31;

        // Act
        var result = TryCreateValidDate(year, month, day);

        // Assert
        result.Should().NotBeNull();
        result!.Value.Year.Should().Be(2025);
        result.Value.Month.Should().Be(12);
        result.Value.Day.Should().Be(31);
    }

    [Fact]
    public void TryCreateValidDate_YearBoundary_January1_Works()
    {
        // Arrange
        int year = 2026, month = 1, day = 1;

        // Act
        var result = TryCreateValidDate(year, month, day);

        // Assert
        result.Should().NotBeNull();
        result!.Value.Year.Should().Be(2026);
        result.Value.Month.Should().Be(1);
        result.Value.Day.Should().Be(1);
    }

    [Fact]
    public void TryCreateValidDate_InvalidMonth_Zero_ReturnsNull()
    {
        // Arrange
        int year = 2025, month = 0, day = 15;

        // Act
        var result = TryCreateValidDate(year, month, day);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void TryCreateValidDate_InvalidMonth_13_ReturnsNull()
    {
        // Arrange
        int year = 2025, month = 13, day = 15;

        // Act
        var result = TryCreateValidDate(year, month, day);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void TryCreateValidDate_InvalidDay_Zero_ReturnsNull()
    {
        // Arrange
        int year = 2025, month = 5, day = 0;

        // Act
        var result = TryCreateValidDate(year, month, day);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void TryCreateValidDate_InvalidDay_Negative_ReturnsNull()
    {
        // Arrange
        int year = 2025, month = 5, day = -5;

        // Act
        var result = TryCreateValidDate(year, month, day);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void TryCreateValidDate_YearTooLow_ReturnsNull()
    {
        // Arrange
        int year = 1899, month = 5, day = 15;

        // Act
        var result = TryCreateValidDate(year, month, day);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void TryCreateValidDate_YearTooHigh_ReturnsNull()
    {
        // Arrange
        int year = 2101, month = 5, day = 15;

        // Act
        var result = TryCreateValidDate(year, month, day);

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData(2025, 4, 31, 30)]  // April has 30 days
    [InlineData(2025, 6, 31, 30)]  // June has 30 days
    [InlineData(2025, 9, 31, 30)]  // September has 30 days
    [InlineData(2025, 11, 31, 30)] // November has 30 days
    public void TryCreateValidDate_Day31_In30DayMonths_ClampsTo30(int year, int month, int day, int expectedDay)
    {
        // Act
        var result = TryCreateValidDate(year, month, day);

        // Assert
        result.Should().NotBeNull();
        result!.Value.Day.Should().Be(expectedDay);
    }

    #endregion

    #region TryCreateValidMonth Tests (Month view)

    [Fact]
    public void TryCreateValidMonth_ValidMonth_ReturnsFirstOfMonth()
    {
        // Arrange
        int year = 2026, month = 6;

        // Act
        var result = TryCreateValidMonth(year, month);

        // Assert
        result.Should().NotBeNull();
        result!.Value.Year.Should().Be(2026);
        result.Value.Month.Should().Be(6);
        result.Value.Day.Should().Be(1);
    }

    [Fact]
    public void TryCreateValidMonth_December_Works()
    {
        // Arrange
        int year = 2025, month = 12;

        // Act
        var result = TryCreateValidMonth(year, month);

        // Assert
        result.Should().NotBeNull();
        result!.Value.Year.Should().Be(2025);
        result.Value.Month.Should().Be(12);
    }

    [Fact]
    public void TryCreateValidMonth_January_Works()
    {
        // Arrange
        int year = 2026, month = 1;

        // Act
        var result = TryCreateValidMonth(year, month);

        // Assert
        result.Should().NotBeNull();
        result!.Value.Year.Should().Be(2026);
        result.Value.Month.Should().Be(1);
    }

    [Fact]
    public void TryCreateValidMonth_InvalidMonth_Zero_ReturnsNull()
    {
        // Arrange
        int year = 2025, month = 0;

        // Act
        var result = TryCreateValidMonth(year, month);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void TryCreateValidMonth_InvalidMonth_13_ReturnsNull()
    {
        // Arrange
        int year = 2025, month = 13;

        // Act
        var result = TryCreateValidMonth(year, month);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void TryCreateValidMonth_YearTooLow_ReturnsNull()
    {
        // Arrange
        int year = 1899, month = 6;

        // Act
        var result = TryCreateValidMonth(year, month);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void TryCreateValidMonth_YearTooHigh_ReturnsNull()
    {
        // Arrange
        int year = 2101, month = 6;

        // Act
        var result = TryCreateValidMonth(year, month);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region DateOnly.AddDays/AddMonths Boundary Tests

    [Fact]
    public void DateOnly_AddDays_CrossesMonthBoundary()
    {
        // Arrange
        var jan31 = new DateOnly(2025, 1, 31);

        // Act
        var feb1 = jan31.AddDays(1);

        // Assert
        feb1.Month.Should().Be(2);
        feb1.Day.Should().Be(1);
    }

    [Fact]
    public void DateOnly_AddDays_CrossesYearBoundary()
    {
        // Arrange
        var dec31 = new DateOnly(2025, 12, 31);

        // Act
        var jan1 = dec31.AddDays(1);

        // Assert
        jan1.Year.Should().Be(2026);
        jan1.Month.Should().Be(1);
        jan1.Day.Should().Be(1);
    }

    [Fact]
    public void DateOnly_AddMonths_HandlesFebruaryCorrectly()
    {
        // Arrange - Jan 31 + 1 month should be Feb 28 (or 29 in leap year)
        var jan31_2025 = new DateOnly(2025, 1, 31);

        // Act
        var result = jan31_2025.AddMonths(1);

        // Assert - .NET's AddMonths clamps to end of month
        result.Month.Should().Be(2);
        result.Day.Should().Be(28); // 2025 is not a leap year
    }

    [Fact]
    public void DateOnly_AddMonths_LeapYear_HandlesFebruaryCorrectly()
    {
        // Arrange - Jan 31, 2024 + 1 month (2024 is a leap year)
        var jan31_2024 = new DateOnly(2024, 1, 31);

        // Act
        var result = jan31_2024.AddMonths(1);

        // Assert
        result.Month.Should().Be(2);
        result.Day.Should().Be(29); // 2024 is a leap year
    }

    [Fact]
    public void DateOnly_AddMonths_CrossesYearBoundary()
    {
        // Arrange
        var dec15 = new DateOnly(2025, 12, 15);

        // Act
        var jan15 = dec15.AddMonths(1);

        // Assert
        jan15.Year.Should().Be(2026);
        jan15.Month.Should().Be(1);
        jan15.Day.Should().Be(15);
    }

    [Fact]
    public void DateOnly_AddMonths_Negative_CrossesYearBoundary()
    {
        // Arrange
        var jan15 = new DateOnly(2026, 1, 15);

        // Act
        var dec15 = jan15.AddMonths(-1);

        // Assert
        dec15.Year.Should().Be(2025);
        dec15.Month.Should().Be(12);
        dec15.Day.Should().Be(15);
    }

    #endregion

    #region Week Start Calculation Tests

    [Theory]
    [InlineData(2025, 1, 15, DayOfWeek.Sunday, 12)]   // Wed Jan 15 -> Sun Jan 12
    [InlineData(2025, 1, 19, DayOfWeek.Sunday, 19)]   // Sun Jan 19 -> Sun Jan 19
    [InlineData(2025, 1, 25, DayOfWeek.Sunday, 19)]   // Sat Jan 25 -> Sun Jan 19
    [InlineData(2025, 2, 1, DayOfWeek.Sunday, 26)]    // Sat Feb 1 -> Sun Jan 26 (crosses month)
    public void WeekStart_CalculatesCorrectSunday(int year, int month, int day, DayOfWeek expectedDayOfWeek, int expectedDay)
    {
        // Arrange
        var target = new DateOnly(year, month, day);

        // Act - Replicate the week start calculation from Week.cshtml.cs
        int daysFromSunday = (int)target.DayOfWeek;
        var weekStart = target.AddDays(-daysFromSunday);

        // Assert
        weekStart.DayOfWeek.Should().Be(expectedDayOfWeek);
        weekStart.Day.Should().Be(expectedDay);
    }

    [Fact]
    public void WeekStart_CrossesYearBoundary()
    {
        // Arrange - Jan 1, 2025 is a Wednesday
        var jan1 = new DateOnly(2025, 1, 1);

        // Act
        int daysFromSunday = (int)jan1.DayOfWeek;
        var weekStart = jan1.AddDays(-daysFromSunday);

        // Assert - Should go back to Dec 29, 2024 (Sunday)
        weekStart.Year.Should().Be(2024);
        weekStart.Month.Should().Be(12);
        weekStart.Day.Should().Be(29);
        weekStart.DayOfWeek.Should().Be(DayOfWeek.Sunday);
    }

    #endregion

    #region Helper Methods (copied from Calendar pages for testing)

    /// <summary>
    /// Safely creates a valid DateOnly from parameters.
    /// Copied from Day.cshtml.cs for isolated testing.
    /// </summary>
    private static DateOnly? TryCreateValidDate(int year, int month, int day)
    {
        if (year < 1900 || year > 2100)
            return null;

        if (month < 1 || month > 12)
            return null;

        if (day < 1)
            return null;

        int daysInMonth = DateTime.DaysInMonth(year, month);

        if (day > daysInMonth)
            day = daysInMonth;

        try
        {
            return new DateOnly(year, month, day);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// Safely creates a valid DateOnly for the first day of a month.
    /// Copied from Month.cshtml.cs for isolated testing.
    /// </summary>
    private static DateOnly? TryCreateValidMonth(int year, int month)
    {
        if (year < 1900 || year > 2100)
            return null;

        if (month < 1 || month > 12)
            return null;

        try
        {
            return new DateOnly(year, month, 1);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    #endregion
}
