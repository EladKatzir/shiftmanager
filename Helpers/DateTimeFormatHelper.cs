using System.Globalization;
using Microsoft.Extensions.Localization;

namespace ShiftManager.Helpers;

/// <summary>
/// Helper class for formatting dates and times according to the current culture.
/// Supports localized date formats for English (en-US) and Hebrew (he-IL).
/// B-010: Date/Time Format Localization
/// </summary>
public static class DateTimeFormatHelper
{
    /// <summary>
    /// Formats date according to current culture
    /// English: "January 30, 2026"
    /// Hebrew: "30 בינואר 2026"
    /// </summary>
    public static string FormatLongDate(DateTime date)
    {
        return date.ToString("d MMMM yyyy", CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// Formats date for calendar headers
    /// English: "January 2026"
    /// Hebrew: "ינואר 2026"
    /// </summary>
    public static string FormatMonthYear(DateTime date)
    {
        return date.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// Formats short date
    /// English: "1/30/2026"
    /// Hebrew: "30/1/2026"
    /// </summary>
    public static string FormatShortDate(DateTime date)
    {
        return date.ToString("d", CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// Formats time in 24-hour format (HH:mm)
    /// </summary>
    public static string FormatTime(DateTime time)
    {
        return time.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Formats time with seconds (HH:mm:ss)
    /// </summary>
    public static string FormatTimeLong(DateTime time)
    {
        return time.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Formats date and time together
    /// English: "January 30, 2026 14:30"
    /// Hebrew: "30 בינואר 2026 14:30"
    /// </summary>
    public static string FormatDateTime(DateTime dateTime)
    {
        return $"{FormatLongDate(dateTime)} {FormatTime(dateTime)}";
    }

    /// <summary>
    /// Gets relative time string (e.g., "5 minutes ago" / "לפני 5 דקות")
    /// </summary>
    /// <param name="dateTime">The datetime to compare against now</param>
    /// <param name="localizer">String localizer for retrieving localized strings</param>
    /// <returns>Localized relative time string</returns>
    public static string GetRelativeTime(DateTime dateTime, IStringLocalizer localizer)
    {
        var now = DateTime.UtcNow;
        var diff = now - dateTime;

        // Just now (less than 60 seconds)
        if (diff.TotalSeconds < 60)
            return localizer["DateTime_JustNow"];

        // Minutes ago (less than 60 minutes)
        if (diff.TotalMinutes < 60)
            return string.Format(localizer["DateTime_MinutesAgo"], (int)diff.TotalMinutes);

        // Hours ago (less than 24 hours)
        if (diff.TotalHours < 24)
            return string.Format(localizer["DateTime_HoursAgo"], (int)diff.TotalHours);

        // Days ago (less than 7 days)
        if (diff.TotalDays < 7)
        {
            int days = (int)diff.TotalDays;
            if (days == 1)
                return string.Format(localizer["DateTime_Yesterday"], FormatTime(dateTime));
            return string.Format(localizer["DateTime_DaysAgo"], days);
        }

        // Weeks ago (less than 30 days)
        if (diff.TotalDays < 30)
            return string.Format(localizer["DateTime_WeeksAgo"], (int)(diff.TotalDays / 7));

        // For older dates, return the formatted long date
        return FormatLongDate(dateTime);
    }

    /// <summary>
    /// Gets a friendly date string (Today, Tomorrow, Yesterday, or the formatted date)
    /// </summary>
    public static string GetFriendlyDate(DateTime date, IStringLocalizer localizer)
    {
        var today = DateTime.Today;
        var dateOnly = date.Date;

        if (dateOnly == today)
            return localizer["DateTime_Today"];

        if (dateOnly == today.AddDays(1))
            return localizer["DateTime_Tomorrow"];

        if (dateOnly == today.AddDays(-1))
            return string.Format(localizer["DateTime_Yesterday"], FormatTime(date));

        return FormatLongDate(date);
    }

    /// <summary>
    /// Gets the day name for the current culture
    /// English: "Monday", "Tuesday", etc.
    /// Hebrew: "יום שני", "יום שלישי", etc.
    /// </summary>
    public static string GetDayName(DayOfWeek day)
    {
        return CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(day);
    }

    /// <summary>
    /// Gets abbreviated day name
    /// English: "Mon", "Tue", etc.
    /// Hebrew: "ב'", "ג'", etc.
    /// </summary>
    public static string GetShortDayName(DayOfWeek day)
    {
        return CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(day);
    }

    /// <summary>
    /// Gets the month name for the current culture
    /// </summary>
    public static string GetMonthName(int month)
    {
        return CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(month);
    }

    /// <summary>
    /// Gets abbreviated month name
    /// </summary>
    public static string GetShortMonthName(int month)
    {
        return CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(month);
    }

    /// <summary>
    /// Gets the first day of week for current culture
    /// Most cultures: Monday
    /// US English: Sunday
    /// Hebrew: Sunday
    /// </summary>
    public static DayOfWeek GetFirstDayOfWeek()
    {
        return CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
    }

    /// <summary>
    /// Gets an array of day names starting from the culture's first day of week
    /// </summary>
    public static string[] GetOrderedDayNames()
    {
        var firstDay = GetFirstDayOfWeek();
        var dayNames = CultureInfo.CurrentCulture.DateTimeFormat.DayNames;
        var orderedNames = new string[7];

        for (int i = 0; i < 7; i++)
        {
            orderedNames[i] = dayNames[((int)firstDay + i) % 7];
        }

        return orderedNames;
    }

    /// <summary>
    /// Gets an array of abbreviated day names starting from the culture's first day of week
    /// </summary>
    public static string[] GetOrderedShortDayNames()
    {
        var firstDay = GetFirstDayOfWeek();
        var dayNames = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames;
        var orderedNames = new string[7];

        for (int i = 0; i < 7; i++)
        {
            orderedNames[i] = dayNames[((int)firstDay + i) % 7];
        }

        return orderedNames;
    }

    /// <summary>
    /// Formats a date range
    /// Same month: "January 15-30, 2026" / "15-30 בינואר 2026"
    /// Different months: "January 15 - February 10, 2026"
    /// </summary>
    public static string FormatDateRange(DateTime start, DateTime end)
    {
        if (start.Year == end.Year && start.Month == end.Month)
        {
            // Same month: "January 15-30, 2026"
            var isHebrew = CultureInfo.CurrentCulture.Name.StartsWith("he");
            if (isHebrew)
            {
                return $"{start.Day}-{end.Day} {GetMonthName(start.Month)} {start.Year}";
            }
            return $"{GetMonthName(start.Month)} {start.Day}-{end.Day}, {start.Year}";
        }
        else
        {
            // Different months
            return $"{FormatLongDate(start)} - {FormatLongDate(end)}";
        }
    }

    /// <summary>
    /// Formats a time range
    /// Example: "08:00 - 16:00"
    /// </summary>
    public static string FormatTimeRange(DateTime start, DateTime end)
    {
        return $"{FormatTime(start)} - {FormatTime(end)}";
    }

    /// <summary>
    /// Formats a time range from TimeSpan
    /// Example: "08:00 - 16:00"
    /// </summary>
    public static string FormatTimeRange(TimeSpan start, TimeSpan end)
    {
        return $"{start:hh\\:mm} - {end:hh\\:mm}";
    }
}
