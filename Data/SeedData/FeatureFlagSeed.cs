using ShiftManager.Models;

namespace ShiftManager.Data.SeedData;

/// <summary>
/// Seed data for initial feature flags.
/// All flags are created as global (no CompanyId/UserId) and disabled by default.
/// </summary>
public static class FeatureFlagSeed
{
    /// <summary>
    /// Gets the initial feature flags for the UI Overhaul project.
    /// </summary>
    public static List<FeatureFlag> GetFeatureFlags()
    {
        var now = DateTime.UtcNow;

        return new List<FeatureFlag>
        {
            new FeatureFlag
            {
                Name = "FF_NEW_NAV_ENABLED",
                IsEnabled = false,
                Description = "Enables the new navigation system with collapsible sidebar and mobile-responsive menu",
                CompanyId = null,
                UserId = null,
                CreatedAt = now,
                UpdatedAt = now
            },
            new FeatureFlag
            {
                Name = "FF_SCOPE_SWITCHER_ENABLED",
                IsEnabled = false,
                Description = "Enables the organizational scope switcher component for navigating hierarchy levels (Project/Area/Molecule/Company)",
                CompanyId = null,
                UserId = null,
                CreatedAt = now,
                UpdatedAt = now
            },
            new FeatureFlag
            {
                Name = "FF_NEW_CALENDAR_STYLES",
                IsEnabled = false,
                Description = "Enables the new calendar styles with improved visual design and print support",
                CompanyId = null,
                UserId = null,
                CreatedAt = now,
                UpdatedAt = now
            },
            new FeatureFlag
            {
                Name = "FF_WIDGETS_ENABLED",
                IsEnabled = false,
                Description = "Enables the dashboard widgets system (On-Call Widget, Quick Actions, etc.)",
                CompanyId = null,
                UserId = null,
                CreatedAt = now,
                UpdatedAt = now
            },
            // Excel Calendar feature flags
            new FeatureFlag
            {
                Name = "FF_EXCEL_CALENDARS",
                IsEnabled = false,
                Description = "Master flag for all Excel-like table calendars. When enabled, allows individual calendar flags to take effect.",
                CompanyId = null,
                UserId = null,
                CreatedAt = now,
                UpdatedAt = now
            },
            new FeatureFlag
            {
                Name = "FF_EXCEL_CALENDAR_SHIFTS",
                IsEnabled = false,
                Description = "Enables the Excel-like Shifts calendar with molecule/job-type scoping and real-time updates.",
                CompanyId = null,
                UserId = null,
                CreatedAt = now,
                UpdatedAt = now
            },
            new FeatureFlag
            {
                Name = "FF_EXCEL_CALENDAR_CHORES",
                IsEnabled = false,
                Description = "Enables the Excel-like Chores calendar with molecule scoping.",
                CompanyId = null,
                UserId = null,
                CreatedAt = now,
                UpdatedAt = now
            },
            new FeatureFlag
            {
                Name = "FF_EXCEL_CALENDAR_ONCALL",
                IsEnabled = false,
                Description = "Enables the Excel-like On-Call calendar with area scoping.",
                CompanyId = null,
                UserId = null,
                CreatedAt = now,
                UpdatedAt = now
            },
            new FeatureFlag
            {
                Name = "FF_EXCEL_CALENDAR_OVERVIEW",
                IsEnabled = false,
                Description = "Enables the Excel-like Overview calendar with company-wide view.",
                CompanyId = null,
                UserId = null,
                CreatedAt = now,
                UpdatedAt = now
            }
        };
    }

    /// <summary>
    /// Known feature flag names for type-safe access.
    /// </summary>
    public static class Flags
    {
        public const string NewNavEnabled = "FF_NEW_NAV_ENABLED";
        public const string ScopeSwitcherEnabled = "FF_SCOPE_SWITCHER_ENABLED";
        public const string NewCalendarStyles = "FF_NEW_CALENDAR_STYLES";
        public const string WidgetsEnabled = "FF_WIDGETS_ENABLED";

        // Excel Calendar flags
        public const string ExcelCalendars = "FF_EXCEL_CALENDARS";
        public const string ExcelCalendarShifts = "FF_EXCEL_CALENDAR_SHIFTS";
        public const string ExcelCalendarChores = "FF_EXCEL_CALENDAR_CHORES";
        public const string ExcelCalendarOnCall = "FF_EXCEL_CALENDAR_ONCALL";
        public const string ExcelCalendarOverview = "FF_EXCEL_CALENDAR_OVERVIEW";
    }
}
