using ShiftManager.Models;

namespace ShiftManager.Data.SeedData;

/// <summary>
/// Seed data for initial feature flags.
/// All flags are created as global (no CompanyId/UserId) and enabled by default.
/// Production recommended values are documented in appsettings.Production.json under "FeatureFlagDefaults".
/// After deployment, enable flags via OwnerHub > Feature Flags page.
/// Seeding is IDEMPOTENT — only flags that don't already exist in the DB are inserted (keyed by Name).
/// </summary>
public static class FeatureFlagSeed
{
    /// <summary>
    /// Gets the initial feature flags.
    /// Convention: all seeded with IsEnabled: true. Disable in production via Owner > Feature Flags as needed.
    /// </summary>
    public static List<FeatureFlag> GetFeatureFlags()
    {
        var now = DateTime.UtcNow;

        return new List<FeatureFlag>
        {
            // ============================================================
            // UI Feature Flags
            // ============================================================
            // NOTE: NewNavEnabled, ScopeSwitcherEnabled, NewCalendarStyles removed — features are permanently active, no fallback UI exists.
            F(Flags.WidgetsEnabled, "Enables the dashboard widgets system (On-Call Widget, Quick Actions, etc.)", now),

            // ============================================================
            // Excel Calendar Feature Flags (existing)
            // ============================================================
            F(Flags.ExcelCalendars, "Master flag for all Excel-like table calendars. When enabled, allows individual calendar flags to take effect.", now),
            F(Flags.ExcelCalendarShifts, "Enables the Excel-like Shifts calendar with molecule/job-type scoping and real-time updates.", now),
            F(Flags.ExcelCalendarChores, "Enables the Excel-like Chores calendar with molecule scoping.", now),
            F(Flags.ExcelCalendarOnCall, "Enables the Excel-like On-Call calendar with area scoping.", now),
            F(Flags.ExcelCalendarOverview, "Enables the Excel-like Overview calendar with company-wide view.", now),

            // ============================================================
            // Operational Feature Flags
            // ============================================================
            // NOTE: EnforceCompanyScope removed from DB seeding — CompanyIdInterceptor reads this
            // from IConfiguration["Features:EnforceCompanyScope"] (Singleton/Scoped DI conflict).
            // The DB flag (FF_ENFORCE_COMPANY_SCOPE) was disconnected and toggling it had no effect.
            F(Flags.AllowPublicSignup, "Allows unauthenticated users to register accounts via the public signup page.", now),
            F(Flags.EnableDailyNotifications, "Enables the daily notification digest background job.", now),
            F(Flags.EnableDirectorRole, "Enables Director role features including cross-company visibility and director-specific UI.", now),
            F(Flags.EnableApiKeyManagement, "Enables API key management in admin panel for external REST API access.", now),
            F(Flags.EnableCompanySwitcher, "Enables the company switcher UI in the navigation for users with multi-company access.", now),
            F(Flags.EnforceRankEligibility, "Enforces military rank eligibility checks during duty rotation and on-duty assignment.", now),

            // ============================================================
            // API Endpoint Feature Flags
            // ============================================================
            F(Flags.ApiEnabled, "Master flag for the REST API. When disabled, all /api/v1/* endpoints return 404.", now),

            // Users API
            F(Flags.ApiUsersList, "Enables GET /api/v1/users — list users with pagination.", now),
            F(Flags.ApiUsersGet, "Enables GET /api/v1/users/{id} — get user details.", now),
            F(Flags.ApiUsersCreate, "Enables POST /api/v1/users — create new user.", now),
            F(Flags.ApiUsersUpdate, "Enables PUT /api/v1/users/{id} — update user.", now),

            // Shifts API
            F(Flags.ApiShiftsList, "Enables GET /api/v1/shifts — list shifts with pagination.", now),
            F(Flags.ApiShiftsGet, "Enables GET /api/v1/shifts/{id} — get shift details.", now),

            // Time Off API
            F(Flags.ApiTimeOffList, "Enables GET /api/v1/time-off-requests — list time-off requests.", now),
            F(Flags.ApiTimeOffGet, "Enables GET /api/v1/time-off-requests/{id} — get time-off request details.", now),
            F(Flags.ApiTimeOffCreate, "Enables POST /api/v1/time-off-requests — create time-off request.", now),
            F(Flags.ApiTimeOffApprove, "Enables POST /api/v1/time-off-requests/{id}/approve — approve time-off request.", now),
            F(Flags.ApiTimeOffDecline, "Enables POST /api/v1/time-off-requests/{id}/decline — decline time-off request.", now),

            // Notifications API
            F(Flags.ApiNotificationsList, "Enables GET /api/v1/notifications — list notifications.", now),
            F(Flags.ApiNotificationsGet, "Enables GET /api/v1/notifications/{id} — get notification details.", now),
            F(Flags.ApiNotificationsMarkRead, "Enables POST /api/v1/notifications/{id}/mark-read — mark notification as read.", now),
            F(Flags.ApiNotificationsMarkAllRead, "Enables POST /api/v1/notifications/mark-all-read — mark all notifications as read.", now),

            // Chores API
            F(Flags.ApiChoresList, "Enables GET /api/v1/chores — list chores.", now),
            F(Flags.ApiChoresGet, "Enables GET /api/v1/chores/{id} — get chore details.", now),
            F(Flags.ApiChoresCreate, "Enables POST /api/v1/chores — create chore.", now),
            F(Flags.ApiChoresUpdate, "Enables PUT /api/v1/chores/{id} — update chore.", now),
            F(Flags.ApiChoresDelete, "Enables DELETE /api/v1/chores/{id} — delete chore.", now),

            // On-Duty API
            F(Flags.ApiOnDutyList, "Enables GET /api/v1/on-duty — list on-duty entries.", now),
            F(Flags.ApiOnDutyGet, "Enables GET /api/v1/on-duty/{id} — get on-duty entry details.", now),
            F(Flags.ApiOnDutyCreate, "Enables POST /api/v1/on-duty — create on-duty entry.", now),
            F(Flags.ApiOnDutyUpdate, "Enables PUT /api/v1/on-duty/{id} — update on-duty entry.", now),
            F(Flags.ApiOnDutyDelete, "Enables DELETE /api/v1/on-duty/{id} — delete on-duty entry.", now),

            // Swap Requests API
            F(Flags.ApiSwapRequestsList, "Enables GET /api/v1/swap-requests — list swap requests.", now),
            F(Flags.ApiSwapRequestsGet, "Enables GET /api/v1/swap-requests/{id} — get swap request details.", now),
            F(Flags.ApiSwapRequestsCreate, "Enables POST /api/v1/swap-requests — create swap request.", now),
            F(Flags.ApiSwapRequestsApprove, "Enables POST /api/v1/swap-requests/{id}/approve — approve swap request.", now),
            F(Flags.ApiSwapRequestsDecline, "Enables POST /api/v1/swap-requests/{id}/decline — decline swap request.", now),
            F(Flags.ApiSwapRequestsDelete, "Enables DELETE /api/v1/swap-requests/{id} — delete swap request.", now),

            // Feedback API
            F(Flags.ApiFeedbackList, "Enables GET /api/v1/feedback — list feedback entries.", now),
            F(Flags.ApiFeedbackGet, "Enables GET /api/v1/feedback/{id} — get feedback details.", now),
            F(Flags.ApiFeedbackCreate, "Enables POST /api/v1/feedback — create feedback.", now),
            F(Flags.ApiFeedbackUpdateStatus, "Enables PATCH /api/v1/feedback/{id}/status — update feedback status.", now),
            F(Flags.ApiFeedbackDelete, "Enables DELETE /api/v1/feedback/{id} — delete feedback.", now),

            // Audit Logs & Analytics API
            F(Flags.ApiAuditLogsList, "Enables GET /api/v1/audit-logs — list audit log entries.", now),
            F(Flags.ApiAnalyticsSummary, "Enables GET /api/v1/analytics/summary — get analytics summary.", now),

            // ============================================================
            // New Feature Flags
            // ============================================================
            F(Flags.FriendshipsEnabled, "Enables the Friendships feature including nav link, friend highlighting on calendars, and friend IDs API.", now),
            F(Flags.DutyRotationEnabled, "Enables the Duty Rotation feature including admin CRUD page and calendar auto-fill.", now),
            F(Flags.SetupTasksEnabled, "Enables the Setup Tasks feature including auto-generation hooks on molecule/company creation.", now),
            F(Flags.VacationApprovalEnabled, "Enables the vacation approval workflow including rule-based routing, auto-approve, and multi-level approval.", now),
            F(Flags.StoreHoursEnabled, "Enables the Store Hours feature including store management, opening hours, and Quick Info widget integration.", now),
            F(Flags.EmailServiceEnabled, "Global kill switch for email notifications. When disabled, no emails are sent regardless of per-company settings.", now),
            F(Flags.HomeUnification, "Enables the unified HOME materialisation, dual-approval routing, and rule-first HomeType. Off = legacy behaviour.", now),

            // Localization flags — read at startup, require an app restart to take effect.
            FDisabled(Flags.HebrewDefault, "When enabled: anonymous visitors default to Hebrew, and legacy en-US cookies from the old default are auto-cleared on next visit. Read ONCE at app startup — toggling this flag in the UI requires an app restart to take effect. Disabled by default for rollback safety.", now),
        };
    }

    /// <summary>
    /// Helper to create a feature flag that seeds DISABLED by default. Used for flags that
    /// modify foundational behavior (e.g. HebrewDefault) where we want explicit opt-in.
    /// </summary>
    private static FeatureFlag FDisabled(string name, string description, DateTime now) => new()
    {
        Name = name,
        IsEnabled = false,
        Description = description,
        CompanyId = null,
        UserId = null,
        CreatedAt = now,
        UpdatedAt = now
    };

    /// <summary>
    /// Helper to create a FeatureFlag with common defaults (global scope, enabled).
    /// </summary>
    private static FeatureFlag F(string name, string description, DateTime now) => new()
    {
        Name = name,
        IsEnabled = true,
        Description = description,
        CompanyId = null,
        UserId = null,
        CreatedAt = now,
        UpdatedAt = now
    };

    /// <summary>
    /// Known feature flag names for type-safe access.
    /// Use these constants throughout the codebase instead of magic strings.
    /// </summary>
    public static class Flags
    {
        // UI flags
        // Removed: NewNavEnabled, ScopeSwitcherEnabled, NewCalendarStyles — permanently active, no fallback UI
        public const string WidgetsEnabled = "FF_WIDGETS_ENABLED";

        // Excel Calendar flags
        public const string ExcelCalendars = "FF_EXCEL_CALENDARS";
        public const string ExcelCalendarShifts = "FF_EXCEL_CALENDAR_SHIFTS";
        public const string ExcelCalendarChores = "FF_EXCEL_CALENDAR_CHORES";
        public const string ExcelCalendarOnCall = "FF_EXCEL_CALENDAR_ONCALL";
        public const string ExcelCalendarOverview = "FF_EXCEL_CALENDAR_OVERVIEW";

        // Operational flags
        public const string EnforceCompanyScope = "FF_ENFORCE_COMPANY_SCOPE";
        public const string AllowPublicSignup = "FF_ALLOW_PUBLIC_SIGNUP";
        public const string EnableDailyNotifications = "FF_ENABLE_DAILY_NOTIFICATIONS";
        public const string EnableDirectorRole = "FF_ENABLE_DIRECTOR_ROLE";
        public const string EnableApiKeyManagement = "FF_ENABLE_API_KEY_MANAGEMENT";
        public const string EnableCompanySwitcher = "FF_ENABLE_COMPANY_SWITCHER";
        public const string EnforceRankEligibility = "FF_ENFORCE_RANK_ELIGIBILITY";

        // API endpoint flags — master
        public const string ApiEnabled = "FF_API_ENABLED";

        // API endpoint flags — Users
        public const string ApiUsersList = "FF_API_USERS_LIST";
        public const string ApiUsersGet = "FF_API_USERS_GET";
        public const string ApiUsersCreate = "FF_API_USERS_CREATE";
        public const string ApiUsersUpdate = "FF_API_USERS_UPDATE";

        // API endpoint flags — Shifts
        public const string ApiShiftsList = "FF_API_SHIFTS_LIST";
        public const string ApiShiftsGet = "FF_API_SHIFTS_GET";

        // API endpoint flags — Time Off
        public const string ApiTimeOffList = "FF_API_TIMEOFF_LIST";
        public const string ApiTimeOffGet = "FF_API_TIMEOFF_GET";
        public const string ApiTimeOffCreate = "FF_API_TIMEOFF_CREATE";
        public const string ApiTimeOffApprove = "FF_API_TIMEOFF_APPROVE";
        public const string ApiTimeOffDecline = "FF_API_TIMEOFF_DECLINE";

        // API endpoint flags — Notifications
        public const string ApiNotificationsList = "FF_API_NOTIFICATIONS_LIST";
        public const string ApiNotificationsGet = "FF_API_NOTIFICATIONS_GET";
        public const string ApiNotificationsMarkRead = "FF_API_NOTIFICATIONS_MARKREAD";
        public const string ApiNotificationsMarkAllRead = "FF_API_NOTIFICATIONS_MARKALLREAD";

        // API endpoint flags — Chores
        public const string ApiChoresList = "FF_API_CHORES_LIST";
        public const string ApiChoresGet = "FF_API_CHORES_GET";
        public const string ApiChoresCreate = "FF_API_CHORES_CREATE";
        public const string ApiChoresUpdate = "FF_API_CHORES_UPDATE";
        public const string ApiChoresDelete = "FF_API_CHORES_DELETE";

        // API endpoint flags — On-Duty
        public const string ApiOnDutyList = "FF_API_ONDUTY_LIST";
        public const string ApiOnDutyGet = "FF_API_ONDUTY_GET";
        public const string ApiOnDutyCreate = "FF_API_ONDUTY_CREATE";
        public const string ApiOnDutyUpdate = "FF_API_ONDUTY_UPDATE";
        public const string ApiOnDutyDelete = "FF_API_ONDUTY_DELETE";

        // API endpoint flags — Swap Requests
        public const string ApiSwapRequestsList = "FF_API_SWAPREQUESTS_LIST";
        public const string ApiSwapRequestsGet = "FF_API_SWAPREQUESTS_GET";
        public const string ApiSwapRequestsCreate = "FF_API_SWAPREQUESTS_CREATE";
        public const string ApiSwapRequestsApprove = "FF_API_SWAPREQUESTS_APPROVE";
        public const string ApiSwapRequestsDecline = "FF_API_SWAPREQUESTS_DECLINE";
        public const string ApiSwapRequestsDelete = "FF_API_SWAPREQUESTS_DELETE";

        // API endpoint flags — Feedback
        public const string ApiFeedbackList = "FF_API_FEEDBACK_LIST";
        public const string ApiFeedbackGet = "FF_API_FEEDBACK_GET";
        public const string ApiFeedbackCreate = "FF_API_FEEDBACK_CREATE";
        public const string ApiFeedbackUpdateStatus = "FF_API_FEEDBACK_UPDATESTATUS";
        public const string ApiFeedbackDelete = "FF_API_FEEDBACK_DELETE";

        // API endpoint flags — Audit Logs & Analytics
        public const string ApiAuditLogsList = "FF_API_AUDITLOGS_LIST";
        public const string ApiAnalyticsSummary = "FF_API_ANALYTICS_SUMMARY";

        // New feature flags
        public const string FriendshipsEnabled = "FF_FRIENDSHIPS_ENABLED";
        public const string DutyRotationEnabled = "FF_DUTY_ROTATION_ENABLED";
        public const string SetupTasksEnabled = "FF_SETUP_TASKS_ENABLED";
        public const string VacationApprovalEnabled = "FF_VACATION_APPROVAL_ENABLED";
        public const string StoreHoursEnabled = "FF_STORE_HOURS_ENABLED";
        public const string EmailServiceEnabled = "FF_EMAIL_SERVICE_ENABLED";
        public const string HomeUnification = "FF_HOME_UNIFICATION";

        // Localization flags (startup-only — require app restart to take effect)
        public const string HebrewDefault = "FF_HEBREW_DEFAULT";
    }
}
