namespace ShiftManager.Models;

/// <summary>
/// A molecule-scoped "tab" (Hebrew: לשונית) — a named sub-calendar within a molecule, like a separate
/// Excel sheet in the legacy system. A <see cref="ShiftType"/> belongs to at most one tab via
/// <see cref="ShiftType.TabId"/> (untagged = the implicit "Main" tab). Companies are assigned to a tab via
/// <see cref="ShiftTabCompany"/>, which drives the by-user roster base.
///
/// A tab is purely a VIEW partition: it filters what the Shifts calendar renders but NEVER narrows
/// eligibility, overlap/rest/weekly-hours (<c>IBusyService</c>), or fairness/Justice — those always reason
/// over a user's full, cross-tab shift set.
///
/// Like <see cref="ShiftCategory"/>, this entity is NOT tenant-filtered (no IBelongsToCompany);
/// visibility is molecule-scoped via MoleculeId, and isolation rests on explicit MoleculeId checks at
/// every call site.
/// </summary>
public class ShiftTab
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public string Name { get; set; } = string.Empty;          // internal name, e.g. "Radio"
    public string DisplayName { get; set; } = string.Empty;   // shown on the tab pill
    public int SortOrder { get; set; }                         // strip order (admin drag-reorder)
    public bool IsActive { get; set; } = true;
    public string? Color { get; set; }                         // optional hex for the active pill, e.g. "#F0C14B"
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Molecule Molecule { get; set; } = null!;
    public List<ShiftType> ShiftTypes { get; set; } = new();       // shift types on this tab (drives shift-view)
    public List<ShiftTabCompany> Companies { get; set; } = new();  // companies on this tab (people-view roster base)
}
