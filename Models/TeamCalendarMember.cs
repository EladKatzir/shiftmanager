namespace ShiftManager.Models;

/// <summary>
/// Many-to-many relationship between TeamCalendars and Users (members).
/// Represents a user selected to appear in a team calendar's week view.
/// </summary>
public class TeamCalendarMember
{
    public int Id { get; set; }

    /// <summary>
    /// The calendar this member belongs to.
    /// </summary>
    public int TeamCalendarId { get; set; }

    /// <summary>
    /// The user who is a member of this calendar (any user in the company).
    /// </summary>
    public int MemberUserId { get; set; }

    /// <summary>
    /// When this member was added to the calendar.
    /// </summary>
    public DateTime AddedAt { get; set; }

    /// <summary>
    /// Optional sort order for manual member ordering (future enhancement).
    /// Default: alphabetical by DisplayName.
    /// </summary>
    public int? SortOrder { get; set; }

    // Navigation properties
    public TeamCalendar TeamCalendar { get; set; } = null!;
    public AppUser Member { get; set; } = null!;
}
