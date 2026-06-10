namespace ShiftManager.Models.Support;

public enum UserRole
{
    Owner = 0,
    Manager = 1,
    Employee = 2,
    Director = 3,
    Trainee = 4,
    Assigner = 5,  // Can edit Chores only, not On-Duty
    AreaAdmin = 6  // Area-level admin, same as Director for authorization
}

public enum RequestStatus
{
    Pending = 0,
    Approved = 1,
    Declined = 2,
    Canceled = 3,
    /// <summary>
    /// Dual-approval intermediate state: one tier (Lead or Director) has approved
    /// but the complementary tier is still pending. Materialiser does NOT fire on this state.
    /// </summary>
    PendingSecondApproval = 4
}

public enum NotificationType
{
    ShiftAdded = 0,
    ShiftRemoved = 1,
    TimeOffApproved = 2,
    TimeOffDeclined = 3,
    SwapRequestApproved = 4,
    SwapRequestDeclined = 5,
    TraineeShadowingAdded = 6,
    TraineeShadowingRemoved = 7,
    EmployeeTraineeAdded = 8,
    EmployeeTraineeRemoved = 9,
    TraineeShadowingCanceledTimeOff = 10,
    TraineeShadowingCanceledRoleChange = 11,
    ChoreAssigned = 12,
    ChoreCanceled = 13,
    OnDutyAssigned = 14,
    OnDutyCanceled = 15,
    TimeOffDeleted = 16,
    FeedbackSubmitted = 17,
    AccessRequestSubmitted = 18,
    AccessRequestApproved = 19
}

/// <summary>
/// Per-user notification engagement mode (notifications overhaul Phase 2).
/// Engaged (default) → every notifiable action emails. Quiet → email only for
/// personally-actionable events; non-actionable accumulate in-app and trigger a single
/// "catch up" email when the unread count crosses the throttle threshold.
/// Toggled via the one-click opt-out link in every email and the NotificationCenter page.
/// </summary>
public enum EngagementMode
{
    Engaged = 0,
    Quiet = 1
}

public enum JoinRequestStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

public enum TimeOffType
{
    Vacation = 0,  // Full vacation: StartDate 00:00 to EndDate+1 13:00
    After = 1,     // Half day: StartDate 16:00 to StartDate+1 13:00
    DayAt = 2      // "Day at [X]" — free-text location in TimeOffRequest.Label. Same date window + approval
                   // flow as Vacation, but tracked separately and NOT counted against the vacation quota (Issue 4)
}

/// <summary>
/// "Day Shift" types (user-facing term: "סוגי משמרות יומיות" / "Day Shift Types").
///
/// TERMINOLOGY NOTE: Enum named "OnDutyType" for historical reasons.
/// In the UI, these appear as types of "Day Shifts".
/// See TERMINOLOGY.md for complete terminology mapping.
/// </summary>
public enum OnDutyType
{
    Hakam = 0,  // חק"מכו - Day Shift: Hakam
    Lead = 1    // מובילתו - Day Shift: Lead
}

/// <summary>
/// Defines the organizational scope at which a ShiftType is defined and managed.
/// Ordered narrowest→broadest to enable safe comparison (scope >= ShiftScope.Molecule = "at least molecule-wide").
/// </summary>
public enum ShiftScope
{
    Company = 0,    // Company-specific, visible molecule-wide but badged with company flair
    Molecule = 1,   // Default for seeded shifts — visible to all in the molecule
    Area = 2        // Cross-molecule, AreaAdmin-level management only
}

public enum EmailTemplateType
{
    ShiftAssigned = 0,
    ShiftChanged = 1,
    ShiftDeleted = 2,
    ChoreAssigned = 3,
    ChoreCanceled = 4,
    TimeOffApproved = 5,
    TimeOffDeclined = 6,
    TimeOffDeleted = 7,
    SwapRequestApproved = 8,
    SwapRequestDeclined = 9,
    OnDutyAssigned = 10,
    OnDutyCanceled = 11,
    AccessRequestSubmitted = 12,
    AccountApproved = 13
}
