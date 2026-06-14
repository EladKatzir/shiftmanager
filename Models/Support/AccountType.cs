namespace ShiftManager.Models.Support;

/// <summary>
/// Account archetype, orthogonal to JobType/Role. Standard = a normal person.
/// Mil = shared reserve (מילואים) account: does shifts, no chores, no Requests, hidden from Overview.
/// GroupUser = administrative shared (יוזר קיבוצי) account: no shifts/chores/assignment, no Requests, hidden from all calendars+analytics.
/// Both Mil and GroupUser retain role-based powers and normal email notifications.
/// </summary>
public enum AccountType
{
    Standard = 0,
    Mil = 1,
    GroupUser = 2
}
