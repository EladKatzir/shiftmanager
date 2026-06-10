using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Fan-out service for multi-company leave requests. When a user does shifts in more than one
/// company, a filed time-off request is mirrored (fan-out) to each additional shift company
/// so each company's approval chain is independent; a single cascade decision then propagates
/// back (Epic 6 Task 4). Single-company users are completely unaffected.
/// </summary>
public interface ILeaveFanoutService
{
    /// <summary>Given a just-saved primary TimeOffRequest, if the user does shifts in MORE THAN ONE
    /// company, assign a shared LeaveGroupId and create one clone per additional shift-company
    /// (explicit CompanyId, same dates/type/reason/label/status). Returns the group id, or null if
    /// no fan-out happened (single shift-company). Returns the list of created clones so the caller
    /// can submit each for approval.</summary>
    Task<(Guid? GroupId, IReadOnlyList<TimeOffRequest> Clones)> FanOutAsync(TimeOffRequest primary, int userId);
}
