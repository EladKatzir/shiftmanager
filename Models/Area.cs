namespace ShiftManager.Models;

public class Area
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Project Project { get; set; } = null!;
    public List<Molecule> Molecules { get; set; } = new();

    public List<JobType> JobTypes { get; set; } = new();

    public AreaSettings? Settings { get; set; }
}
