namespace ShiftManager.Models.Support;

public enum UserRole
{
    Owner = 0,
    Manager = 1,
    Employee = 2,
    Director = 3,
    Trainee = 4
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
    ChoreCanceled = 13
}

public enum JoinRequestStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}
