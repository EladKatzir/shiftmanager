using ShiftManager.Models;

namespace ShiftManager.Models.Support;

/// <summary>
/// Single source of truth for what each AccountType may do / where it is visible.
/// Chokepoints call these for in-memory AppUser instances. Inside EF IQueryable
/// filters use the raw enum comparison instead (these methods don't translate to SQL).
/// </summary>
public static class UserCapabilities
{
    public static bool CanBeAssignedShift(this AppUser u) => u.AccountType != AccountType.GroupUser;
    public static bool CanDoChores(this AppUser u) => u.AccountType == AccountType.Standard;
    public static bool CanBeAssignedAnything(this AppUser u) => u.AccountType != AccountType.GroupUser;
    public static bool CanAccessRequests(this AppUser u) => u.AccountType == AccountType.Standard;
    public static bool CanBeVacationApprover(this AppUser u) => u.AccountType == AccountType.Standard;
    public static bool IsVisibleOnOverview(this AppUser u) => u.AccountType == AccountType.Standard;
    public static bool IsVisibleOnShiftsCalendar(this AppUser u) => u.AccountType != AccountType.GroupUser;
    public static bool IsVisibleOnChoresCalendar(this AppUser u) => u.AccountType == AccountType.Standard;
    public static bool IsVisibleInAnalytics(this AppUser u) => u.AccountType != AccountType.GroupUser;
}
