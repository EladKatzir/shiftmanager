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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Area Area { get; set; } = null!;
    public List<Company> Companies { get; set; } = new();       // Workforce molecules
    public List<Department> Departments { get; set; } = new();  // Tech molecules

    public List<ShiftGrouping> ShiftGroupings { get; set; } = new();

    // TODO: Uncomment when ChoreType entity is created
    // public List<ChoreType> ChoreTypes { get; set; } = new();

    // TODO: Uncomment when MoleculeSettings entity is created
    // public MoleculeSettings? Settings { get; set; }
}
