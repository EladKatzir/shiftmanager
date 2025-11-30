namespace ShiftManager.Models.Support;

public enum UserRole
{
    Owner = 0,
    Manager = 1,
    Employee = 2,
    Director = 3,
    Trainee = 4,
    Assigner = 5  // Can edit Chores only, not On-Duty
}

public enum RequestStatus
{
    Pending = 0,
    Approved = 1,
    Declined = 2
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
    FeedbackSubmitted = 17
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
    After = 1      // Half day: StartDate 16:00 to StartDate+1 13:00
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
