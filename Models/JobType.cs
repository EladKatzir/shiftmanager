namespace ShiftManager.Models;

public class JobType
{
    public int Id { get; set; }
    public int AreaId { get; set; }  // Area-scoped (same JobTypes across molecules in an area)
    /// <summary>
    /// Optional molecule-specific scoping. When null, the job type applies to all molecules
    /// in the area. When set, it only applies to that specific molecule.
    /// </summary>
    public int? MoleculeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Color { get; set; }  // For UI display (hex color code)
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>
    /// When true, this job type only appears for Workforce and Helper molecules.
    /// Tech and System molecules will not see it in dropdowns or assignments.
    /// </summary>
    public bool IsWorkforceOnly { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Area Area { get; set; } = null!;
    public Molecule? Molecule { get; set; }
    public List<AppUser> Users { get; set; } = new();
}
