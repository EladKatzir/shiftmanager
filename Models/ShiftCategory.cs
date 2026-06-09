namespace ShiftManager.Models;

/// <summary>
/// A molecule-scoped, functional grouping of shift elements (e.g. "Yekev", which owns
/// "Yekev Morning", "Yekev Night"). A ShiftType belongs to at most one category via
/// <see cref="ShiftType.CategoryId"/>. Users participate in shifts (AppUser.DoesShifts) and are
/// mapped to one or more categories via <see cref="UserShiftCategory"/> — the basis for grouping
/// the by-user and by-shift calendars.
///
/// This is a DISTINCT axis from <see cref="ShiftGrouping"/> (geographic, e.g. Tzafon/Darom,
/// membership by company/job-type). A shift type may carry both a ShiftGroupingId and a CategoryId.
///
/// Like <see cref="ShiftType"/>, this entity is NOT tenant-filtered (no IBelongsToCompany);
/// visibility is molecule-scoped via MoleculeId.
/// </summary>
public class ShiftCategory
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public string Name { get; set; } = string.Empty;          // e.g. "Yekev"
    public string DisplayName { get; set; } = string.Empty;
    public int SortOrder { get; set; }                         // Accordion ordering in the calendar
    public bool IsActive { get; set; } = true;
    public string? Color { get; set; }                         // Optional hex color for the group header
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Molecule Molecule { get; set; } = null!;
    public List<ShiftType> ShiftTypes { get; set; } = new();           // Shift elements owned by this category
    public List<UserShiftCategory> Members { get; set; } = new();      // Users mapped to this category
}
