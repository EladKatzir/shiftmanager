namespace ShiftManager.Models;

public class ChoreType
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string? NameHe { get; set; }
    public string? Color { get; set; }  // Hex color e.g. "#F0C14B"
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedByUserId { get; set; }

    // Parity additions
    public int? ChoreCategoryId { get; set; }       // nullable: types may be uncategorized (mirrors ShiftType.CategoryId)
    public int? DefaultWeightMinutes { get; set; }  // null → global fallback (480 = 8h) at chore-create time

    // Navigation
    public ChoreCategory? ChoreCategory { get; set; }
    public Molecule Molecule { get; set; } = null!;
    public AppUser CreatedByUser { get; set; } = null!;
    public List<Chore> Chores { get; set; } = new();
}
