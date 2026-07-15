using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Manages owner-scoped <see cref="DeskTeamView"/> saved dynamic team-table views: a
/// (TargetCompany × JobType) filter rendered on the /Calendar/Team page.
///
/// Deliberately SEPARATE from <see cref="TeamCalendarService"/> — TeamCalendar's list endpoint
/// (api/team-calendars) returns ALL of an owner's calendars, so reusing it here would leak these
/// dynamic filter views into the /MyTeam page. /MyTeam, TeamCalendar and TeamCalendarService are
/// not touched by this service.
///
/// All methods resolve the acting user from the current request (ClaimTypes.NameIdentifier) and the
/// current tenant from <see cref="ITenantResolver"/> — mirrors the claim-resolution pattern used by
/// ChoreService, not TeamCalendarService's explicit-ownerId-parameter style.
/// </summary>
public interface IDeskTeamViewService
{
    /// <summary>The current user's non-deleted saved views, ordered by SortOrder then CreatedAt.</summary>
    Task<List<DeskTeamView>> ListForOwnerAsync();

    /// <summary>Creates a new saved view owned by the current user in their current tenant company.</summary>
    Task<DeskTeamView> CreateAsync(int targetCompanyId, int jobTypeId, string name);

    /// <summary>Renames a saved view. Throws <see cref="InvalidOperationException"/> if not found or not owned by the current user.</summary>
    Task RenameAsync(int id, string name);

    /// <summary>Soft-deletes a saved view. Throws <see cref="InvalidOperationException"/> if not found or not owned by the current user.</summary>
    Task DeleteAsync(int id);
}
