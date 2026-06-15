namespace ShiftManager.Models;

/// <summary>
/// A molecule-scoped, functional grouping of chore types (e.g. "Physical", "Computer").
/// A ChoreType belongs to at most one category via <see cref="ChoreType.ChoreCategoryId"/>.
/// Users participate in chores (AppUser.DoesChores) and are mapped to one or more categories
/// via <see cref="UserChoreCategory"/>. Mirrors <see cref="ShiftCategory"/>; NOT tenant-filtered
/// (visibility is molecule-scoped via MoleculeId).
/// </summary>
public class ChoreCategory
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public string Name { get; set; } = string.Empty;          // unique per (MoleculeId, Name)
    public string DisplayName { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string? NameHe { get; set; }
    public string? Color { get; set; }                         // hex; rendered as a dot/accent, never a text bg
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Molecule Molecule { get; set; } = null!;
    public List<ChoreType> ChoreTypes { get; set; } = new();
    public List<UserChoreCategory> Members { get; set; } = new();
}
