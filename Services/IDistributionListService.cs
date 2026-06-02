namespace ShiftManager.Services;

/// <summary>
/// Outcome of a distribution-list mutation. Maps to an HTTP status + a localized message in the handler.
/// </summary>
public enum DistributionListOutcome
{
    Ok,
    Forbidden,       // caller lacks ManageDistributionLists scoped to the list's molecule
    NotFound,        // list does not exist (or not in the expected molecule)
    NameRequired,    // empty/whitespace name
    NameTaken,       // case-insensitive duplicate within the molecule
    NoMembers,       // zero members selected
    InvalidMembers   // a selected user is not in the list's molecule
}

public readonly record struct DistributionListResult(DistributionListOutcome Outcome, int ListId = 0)
{
    public bool Ok => Outcome == DistributionListOutcome.Ok;
}

/// <summary>Lightweight summary for the toolbar dropdown and the manager modal list.</summary>
public record DistributionListSummary(int Id, string Name, int MemberCount);

/// <summary>Full detail for the edit modal.</summary>
public record DistributionListDetail(int Id, string Name, int MoleculeId, List<int> MemberUserIds);

/// <summary>A list plus its ordered member user IDs, used to build the grouped calendar view.</summary>
public record DistributionListWithMembers(int Id, string Name, List<int> MemberUserIds);

/// <summary>
/// Manages molecule-scoped, shared distribution lists used to organize the by-user shift calendar.
/// All reads are molecule-scoped (cross-company within the molecule); mutations are gated by the
/// ManageDistributionLists grant re-checked against the list's stored molecule (IDOR guard).
/// </summary>
public interface IDistributionListService
{
    /// <summary>Lists in a molecule with member counts (ordered by name). Read access is open to all viewers.</summary>
    Task<List<DistributionListSummary>> GetListsForMoleculeAsync(int moleculeId);

    /// <summary>Selected lists with their ordered member user IDs (ordered by list name) — drives the grouped calendar view.</summary>
    Task<List<DistributionListWithMembers>> GetListsWithMembersAsync(IEnumerable<int> listIds, int moleculeId);

    /// <summary>One list's detail for the edit modal, or null if it does not belong to the molecule.</summary>
    Task<DistributionListDetail?> GetListDetailAsync(int listId, int moleculeId);

    /// <summary>
    /// For the editor's member picker: maps each molecule user already in ≥1 list to that list's name(s).
    /// Lets the create/edit modal clearly flag users who are already assigned elsewhere. Users in no list are absent.
    /// </summary>
    Task<Dictionary<int, List<string>>> GetMembershipNamesAsync(int moleculeId);

    /// <summary>Create a list in the given molecule. Re-checks the caller's grant and validates members/name.</summary>
    Task<DistributionListResult> CreateAsync(int actingUserId, int moleculeId, string name, IReadOnlyCollection<int> userIds);

    /// <summary>Update a list (molecule derived from the persisted list). Re-checks grant + validates.</summary>
    Task<DistributionListResult> UpdateAsync(int actingUserId, int listId, string name, IReadOnlyCollection<int> userIds);

    /// <summary>Delete a list (molecule derived from the persisted list). Re-checks grant. Cascades members.</summary>
    Task<DistributionListResult> DeleteAsync(int actingUserId, int listId);
}
