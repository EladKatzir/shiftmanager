using System.Globalization;

namespace ShiftManager.Services
{
    public interface ILocalizationService
    {
        string FormatDate(DateTime date);
        string FormatTime(DateTime time);
        string FormatDateTime(DateTime dateTime);
        string FormatNumber(int number);
        string FormatDecimal(decimal number);
        string FormatCurrency(decimal amount);
        bool IsHebrew { get; }
        CultureInfo CurrentCulture { get; }

        // Date formatting helpers
        string FormatShortDate(DateTime date);
        string FormatLongDate(DateTime date);
        string FormatDayOfWeek(DateTime date);
        string FormatRelativeTime(DateTime dateTime);
        string FormatMediumDate(DateTime date);

        // Month/Year and time formatting
        string FormatMonthYear(DateTime date);
        string FormatMonthYear(DateOnly date);
        string FormatMonthAbbreviation(DateTime date);
        string FormatMonthAbbreviation(DateOnly date);
        string FormatTimeLong(DateTime date);
        string FormatShortMonthDay(DateTime date);
        string FormatShortMonthDay(DateOnly date);

        // DateOnly/TimeOnly overloads
        string FormatMediumDate(DateOnly date);
        string FormatDate(DateOnly date);
        string FormatTime(TimeOnly time);
        string FormatShortDate(DateOnly date);
        string FormatLongDate(DateOnly date);
    }
}