using ShiftManager.Models.Support;

namespace ShiftManager.Models;

public class Molecule
{
    public int Id { get; set; }
    public int AreaId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public MoleculeType Type { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Area Area { get; set; } = null!;
    public List<Company> Companies { get; set; } = new();       // Workforce molecules
    public List<Department> Departments { get; set; } = new();  // Legacy: was used for Tech molecules before convergence

    public List<ShiftGrouping> ShiftGroupings { get; set; } = new();

    public List<ChoreType> ChoreTypes { get; set; } = new();

    public MoleculeSettings? Settings { get; set; }
}
