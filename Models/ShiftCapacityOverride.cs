namespace ShiftManager.Models;

public class ShiftCapacityOverride
{
    public int Id { get; set; }
    public int ShiftTypeId { get; set; }
    public int MoleculeId { get; set; }
    public int JobTypeId { get; set; }
    public DateOnly Date { get; set; }
    public int Capacity { get; set; }
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ShiftType ShiftType { get; set; } = null!;
    public Molecule Molecule { get; set; } = null!;
    public JobType JobType { get; set; } = null!;
    public AppUser CreatedByUser { get; set; } = null!;
}
