using System.ComponentModel.DataAnnotations;

namespace ShiftManager.Models;

/// <summary>
/// A private, named calendar view owned by a user showing their selected team members' weekly status.
/// Each user can create multiple calendars (e.g., "Project X Team", "QA Crew", "Ops Rotation").
/// </summary>
public class TeamCalendar : IBelongsToCompany
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    /// <summary>
    /// Owner of this calendar. Calendars are private and unshareable in v1.
    /// </summary>
    public int OwnerId { get; set; }

    /// <summary>
    /// Calendar name (1-60 characters). Must be unique per owner.
    /// Examples: "My Team", "Project X Team", "QA Crew"
    /// </summary>
    [Required]
    [MaxLength(60)]
    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Soft delete flag. When true, calendar is hidden from user's list.
    /// </summary>
    public bool IsDeleted { get; set; }

    // Navigation properties
    public Company Company { get; set; } = null!;
    public AppUser Owner { get; set; } = null!;
    public ICollection<TeamCalendarMember> Members { get; set; } = new List<TeamCalendarMember>();
}
