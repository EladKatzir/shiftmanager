using System.Globalization;
using Microsoft.AspNetCore.Localization;

namespace ShiftManager.Services
{
    public class LocalizationService : ILocalizationService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public LocalizationService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public bool IsHebrew => CurrentCulture.Name.StartsWith("he");

        public CultureInfo CurrentCulture
        {
            get
            {
                var httpContext = _httpContextAccessor.HttpContext;
                if (httpContext != null)
                {
                    var feature = httpContext.Features.Get<IRequestCultureFeature>();
                    if (feature != null)
                    {
                        return feature.RequestCulture.Culture;
                    }
                }
                return CultureInfo.CurrentCulture;
            }
        }

        public string FormatDate(DateTime date)
        {
            if (IsHebrew)
            {
                // Hebrew date format: day/month/year
                return date.ToString("dd/MM/yyyy", CurrentCulture);
            }
            return date.ToString("MM/dd/yyyy", CurrentCulture);
        }

        public string FormatTime(DateTime time)
        {
            if (IsHebrew)
            {
                // Hebrew uses 24-hour format
                return time.ToString("HH:mm", CurrentCulture);
            }
            return time.ToString("h:mm tt", CurrentCulture);
        }

        public string FormatDateTime(DateTime dateTime)
        {
            if (IsHebrew)
            {
                // Hebrew format: day/month/year hour:minute
                return dateTime.ToString("dd/MM/yyyy HH:mm", CurrentCulture);
            }
            return dateTime.ToString("MM/dd/yyyy h:mm tt", CurrentCulture);
        }

        public string FormatNumber(int number)
        {
            // Both Hebrew and English use the same format string; CurrentCulture handles locale-specific separators
            return number.ToString("N0", CurrentCulture);
        }

        public string FormatDecimal(decimal number)
        {
            // Both Hebrew and English use the same format string; CurrentCulture handles locale-specific separators
            return number.ToString("N2", CurrentCulture);
        }

        public string FormatCurrency(decimal amount)
        {
            if (IsHebrew)
            {
                // Israeli Shekel format
                var hebrewCulture = new CultureInfo("he-IL");
                return amount.ToString("C", hebrewCulture);
            }
            return amount.ToString("C", CurrentCulture);
        }

        // Helper methods for common formatting scenarios
        public string FormatShortDate(DateTime date)
        {
            if (IsHebrew)
            {
                return date.ToString("dd/MM", CurrentCulture);
            }
            return date.ToString("MM/dd", CurrentCulture);
        }

        public string FormatLongDate(DateTime date)
        {
            if (IsHebrew)
            {
                // Use Hebrew month names from resources
                var hebrewCulture = new CultureInfo("he-IL");
                return date.ToString("dd MMMM yyyy", hebrewCulture);
            }
            return date.ToString("MMMM dd, yyyy", CurrentCulture);
        }

        public string FormatDayOfWeek(DateTime date)
        {
            if (IsHebrew)
            {
                var hebrewCulture = new CultureInfo("he-IL");
                return date.ToString("dddd", hebrewCulture);
            }
            return date.ToString("dddd", CurrentCulture);
        }

        public string FormatRelativeTime(DateTime dateTime)
        {
            if (dateTime.Kind == DateTimeKind.Local)
                dateTime = dateTime.ToUniversalTime();

            var now = DateTime.UtcNow;
            var diff = now - dateTime;

            if (IsHebrew)
            {
                if (diff.TotalMinutes < 1)
                    return "עכשיו";
                if (diff.TotalMinutes < 60)
                    return $"לפני {(int)diff.TotalMinutes} דקות";
                if (diff.TotalHours < 24)
                    return $"לפני {(int)diff.TotalHours} שעות";
                if (diff.TotalDays < 7)
                    return $"לפני {(int)diff.TotalDays} ימים";

                return FormatDate(dateTime);
            }
            else
            {
                if (diff.TotalMinutes < 1)
                    return "now";
                if (diff.TotalMinutes < 60)
                    return $"{(int)diff.TotalMinutes} minutes ago";
                if (diff.TotalHours < 24)
                    return $"{(int)diff.TotalHours} hours ago";
                if (diff.TotalDays < 7)
                    return $"{(int)diff.TotalDays} days ago";

                return FormatDate(dateTime);
            }
        }
    }
}