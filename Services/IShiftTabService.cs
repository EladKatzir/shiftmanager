using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Manages molecule-scoped <see cref="ShiftTab"/> entities (Hebrew: לשונית) — named sub-calendars within a
/// molecule. A tab has two admin-set memberships: shift types (<see cref="ShiftType.TabId"/>, drives the
/// shift-view) and companies (<see cref="ShiftTabCompany"/>, drives the by-user roster base). Untagged shift
/// types + unassigned companies form the implicit "Main" tab. Also owns per-user "last tab" memory
/// (<see cref="UserShiftTabPreference"/>).
///
/// A tab is a VIEW partition only — it never affects eligibility, overlap/rest/hours, or fairness.
/// </summary>
public interface IShiftTabService
{
    // ---- Tab queries ----
    Task<List<ShiftTab>> GetTabsForMoleculeAsync(int moleculeId, bool includeInactive = false);
    Task<ShiftTab?> GetTabAsync(int tabId);

    // ---- Tab CRUD ----
    /// <summary>Creates a tab. Returns null if the (molecule, name) pair already exists or name is blank.</summary>
    Task<ShiftTab?> CreateAsync(int moleculeId, string name, string displayName, string? color = null);
    /// <summary>Renames/recolors a tab. Returns false on not-found or a (molecule, name) collision.</summary>
    Task<bool> RenameAsync(int tabId, string name, string displayName, string? color);
    /// <summary>
    /// Hard-deletes a tab: its shift types' TabId is nulled (FK SetNull → back to Main), its company links
    /// cascade away, and any remembered preference pointing at it reverts to Main (FK SetNull).
    /// </summary>
    Task<bool> DeleteAsync(int tabId);
    /// <summary>Rewrites tab SortOrder from the given order. Returns false if any id is not in the molecule.</summary>
    Task<bool> ReorderTabsAsync(int moleculeId, IReadOnlyList<int> orderedTabIds);

    /// <summary>Counts the shift types tagged to, and companies assigned to, a tab (for delete-impact preview).</summary>
    Task<(int ShiftTypeCount, int CompanyCount)> GetUsageAsync(int tabId);

    // ---- Membership assignment ----
    /// <summary>
    /// Sets (or clears, when tabId is null) a shift type's tab. Rejects area-scoped shifts (they span
    /// molecules → always Main) and any tab that isn't in the shift's molecule. Returns false on those.
    /// </summary>
    Task<bool> AssignShiftTypeToTabAsync(int shiftTypeId, int? tabId);
    /// <summary>
    /// Assigns a company to a tab (upsert — moves it off any prior tab), or clears it when tabId is null.
    /// Rejects a company whose molecule differs from the tab's (cross-molecule stitching guard). A company
    /// is on at most one tab (unique CompanyId).
    /// </summary>
    Task<bool> AssignCompanyToTabAsync(int companyId, int? tabId);

    /// <summary>
    /// The company set that defines a tab's people-view roster base. For a specific tab: the companies
    /// assigned to it. For Main (tabId null): the molecule's companies with no tab assignment.
    /// </summary>
    Task<HashSet<int>> GetCompanyIdsForTabAsync(int moleculeId, int? tabId);

    // ---- Per-user last-tab memory ----
    /// <summary>The user's remembered tab for a molecule (null = Main, or no preference yet).</summary>
    Task<int?> GetLastTabAsync(int userId, int moleculeId);
    /// <summary>Upserts the user's remembered tab for a molecule (null = explicitly Main).</summary>
    Task SetLastTabAsync(int userId, int moleculeId, int? tabId);
}
