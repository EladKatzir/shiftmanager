using System.ComponentModel.DataAnnotations;

namespace ShiftManager.Models;

/// <summary>
/// A private, owner-scoped saved "team table" view: a (TargetCompany × JobType) filter used to
/// render a dynamic roster on the /Calendar/Team page.
///
/// Deliberately a SEPARATE entity from <see cref="TeamCalendar"/>: TeamCalendar's list endpoint
/// (api/team-calendars) returns ALL of an owner's calendars, so extending it here would leak these
/// dynamic filter views into the /MyTeam page. /MyTeam, TeamCalendar and TeamCalendarService are not
/// modified by this feature.
/// </summary>
public class DeskTeamView : IBelongsToCompany
{
    public int Id { get; set; }

    /// <summary>Tenant — the owner's current company at creation time.</summary>
    public int CompanyId { get; set; }

    /// <summary>Owner of this saved view. Private and unshareable, like TeamCalendar.</summary>
    public int OwnerId { get; set; }

    /// <summary>The company whose users this view filters to.</summary>
    public int TargetCompanyId { get; set; }

    /// <summary>The job-type filter for this view.</summary>
    public int JobTypeId { get; set; }

    /// <summary>
    /// View name (1-60 characters). Must be unique per owner among non-deleted views.
    /// </summary>
    [Required]
    [MaxLength(60)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional manual sort order among the owner's saved views.</summary>
    public int? SortOrder { get; set; }

    /// <summary>
    /// Soft delete flag. When true, the view is hidden from the owner's list.
    /// </summary>
    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public Company Company { get; set; } = null!;
    public AppUser Owner { get; set; } = null!;
}
