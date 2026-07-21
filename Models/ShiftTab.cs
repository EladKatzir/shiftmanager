namespace ShiftManager.Models;

/// <summary>
/// A calendar "tab" (Hebrew: לשונית) scoped to exactly one <c>(Molecule, JobType)</c> — a named
/// sub-calendar within a molecule's job-type calendar. Companies join a tab via <see cref="ShiftTabCompany"/>
/// (people-view roster base + selector prioritization) and shift types via <see cref="ShiftTabShiftType"/>
/// (by-shift view). Both memberships are many-to-many; empty = "no restriction" (all). There is no stored
/// "Main" tab — the always-present synthetic "All" view is view-only (no DB row).
///
/// A tab is purely a VIEW/relevance partition: it filters what the Shifts calendar renders and PRIORITIZES
/// pickers, but NEVER narrows eligibility, overlap/rest/weekly-hours, or fairness/Justice.
///
/// NOT tenant-filtered (no IBelongsToCompany); visibility is molecule-scoped via MoleculeId, isolation rests
/// on explicit MoleculeId checks at every call site.
/// </summary>
public class ShiftTab
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    /// <summary>The job type this tab belongs to. Null only for Tech molecules (calendar job type is null).</summary>
    public int? JobTypeId { get; set; }
    public string NameEn { get; set; } = string.Empty;   // English display name (required)
    public string NameHe { get; set; } = string.Empty;   // Hebrew display name (required)
    /// <summary>Prioritize this tab's companies' users in the assignment pickers (+ off-tab warning).</summary>
    public bool PrioritizeCompanyUsers { get; set; } = true;
    public string? Color { get; set; }                    // optional hex for the active pill, e.g. "#F0C14B"
    public int SortOrder { get; set; }                    // stable strip order (no drag-drop UI)
    public bool IsActive { get; set; } = true;

    // Audit
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Navigation
    public Molecule Molecule { get; set; } = null!;
    public JobType? JobType { get; set; }
    public List<ShiftTabShiftType> ShiftTypes { get; set; } = new();   // shift types on this tab
    public List<ShiftTabCompany> Companies { get; set; } = new();      // companies on this tab
}
