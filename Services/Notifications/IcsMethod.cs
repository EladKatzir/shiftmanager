namespace ShiftManager.Services.Notifications;

/// <summary>
/// iCalendar METHOD for a calendar-eligible event. Consumed by the ICS builder in Phase 4–5.
/// </summary>
public enum IcsMethod
{
    Request,  // create / invite
    Update,   // modify existing (same UID, SEQUENCE++)
    Cancel    // remove
}
