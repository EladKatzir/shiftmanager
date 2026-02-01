namespace ShiftManager.Models;

public class MoleculeSettings
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public int? RestHoursOverride { get; set; }
    public int? WeeklyCapOverride { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int? UpdatedByUserId { get; set; }

    // Navigation
    public Molecule Molecule { get; set; } = null!;
    public AppUser? UpdatedByUser { get; set; }
}
