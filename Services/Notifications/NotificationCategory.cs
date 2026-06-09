namespace ShiftManager.Services.Notifications;

/// <summary>
/// Grouping used for per-category user mutes (Phase 2) and digest sectioning.
/// Append new values at the end — values may be persisted in NotificationCategoryMute.
/// </summary>
public enum NotificationCategory
{
    ShiftAssignment = 0,
    ShiftChange = 1,
    Chore = 2,
    OnDuty = 3,
    Swap = 4,
    TimeOff = 5,
    Trainee = 6,
    Account = 7,
    AccountSecurity = 8,
    AccessRequest = 9,
    CalendarEntry = 10,
    Social = 11,
    Feedback = 12,
    System = 13
}
