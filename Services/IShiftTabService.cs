using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Manages <c>(Molecule, JobType)</c>-scoped <see cref="ShiftTab"/> entities (Hebrew: לשונית). A tab has two
/// admin-set memberships: shift types (<see cref="ShiftTabShiftType"/>, by-shift view) and companies
/// (<see cref="ShiftTabCompany"/>, people-view roster base + prioritization). Both are many-to-many; empty =
/// no restriction (all). Also owns per-user "last tab" memory (<see cref="UserShiftTabPreference"/>).
///
/// A tab is a VIEW/relevance partition only — it never affects eligibility, overlap/rest/hours, or fairness.
/// </summary>
public interface IShiftTabService
{
    // ---- Tab queries ----
    Task<List<ShiftTab>> GetTabsForMoleculeAsync(int moleculeId, int? jobTypeId, bool includeInactive = false);
    Task<ShiftTab?> GetTabAsync(int tabId);

    // ---- Tab CRUD ----
    /// <summary>Creates a tab. Returns null if NameEn OR NameHe already exists in the (molecule, jobtype)
    /// scope, or either name is blank. New tabs default PrioritizeCompanyUsers = true.</summary>
    Task<ShiftTab?> CreateAsync(int moleculeId, int? jobTypeId, string nameEn, string nameHe,
        string? color = null, int? createdByUserId = null);
    /// <summary>Edits name/color/prioritize. Returns false on not-found or a NameEn/NameHe collision.</summary>
    Task<bool> RenameAsync(int tabId, string nameEn, string nameHe, string? color, bool prioritizeCompanyUsers);
    /// <summary>Hard-deletes a tab: company + shift-type join rows cascade away; remembered prefs → SetNull.</summary>
    Task<bool> DeleteAsync(int tabId);
    /// <summary>Counts the shift-type and company join rows for a tab (delete-impact preview).</summary>
    Task<(int ShiftTypeCount, int CompanyCount)> GetUsageAsync(int tabId);

    // ---- Membership (many-to-many, replace-set) ----
    /// <summary>Replaces the tab's company set. Rejects (returns false, no change) if ANY company is not in the
    /// tab's molecule (IDOR guard). Empty = no restriction.</summary>
    Task<bool> SetCompaniesForTabAsync(int tabId, IReadOnlyCollection<int> companyIds);
    /// <summary>Replaces the tab's shift-type set. Rejects (returns false, no change) if ANY shift type is not
    /// in the tab's molecule or its jobtype scope (jobtype match or null). Empty = no restriction.</summary>
    Task<bool> SetShiftTypesForTabAsync(int tabId, IReadOnlyCollection<int> shiftTypeIds);
    /// <summary>The companies assigned to a tab (empty = no restriction).</summary>
    Task<HashSet<int>> GetCompanyIdsForTabAsync(int tabId);
    /// <summary>The shift types assigned to a tab (empty = no restriction).</summary>
    Task<HashSet<int>> GetShiftTypeIdsForTabAsync(int tabId);

    // ---- Phase E: calendar-integration projections ----
    /// <summary>
    /// shiftTypeId → the set of tab ids whose EFFECTIVE shift-type set contains it, for one (molecule,
    /// jobtype). A tab with an explicit selection contributes those ids; a tab with NO selection expands
    /// to every molecule+jobtype shift type ("empty = all"); HOME/OFFLINE + null-jobtype shared shift
    /// types map to EVERY tab (PF7 — never ghosted / never hidden). Powers by-user cross-over + ghosting.
    /// </summary>
    Task<Dictionary<int, HashSet<int>>> GetShiftTypeTabMapAsync(int moleculeId, int? jobTypeId);

    /// <summary>
    /// The subset of <paramref name="userIds"/> that belong — via ANY CompanyMembership — to the tab's
    /// company set. Multi-company aware (a user in-tab through a non-primary membership counts). null tab,
    /// or a tab with no companies, → empty (no prioritization). SECURITY: caller gates molecule access.
    /// </summary>
    Task<HashSet<int>> GetInTabUserIdsAsync(int? tabId, IReadOnlyCollection<int> userIds);

    // ---- Per-user last-tab memory ----
    /// <summary>The user's remembered tab for a (molecule, jobtype) (null = none / the "All" view).</summary>
    Task<int?> GetLastTabAsync(int userId, int moleculeId, int? jobTypeId);
    /// <summary>Upserts the user's remembered tab for a (molecule, jobtype) (null = explicit "All").</summary>
    Task SetLastTabAsync(int userId, int moleculeId, int? jobTypeId, int? tabId);
}
