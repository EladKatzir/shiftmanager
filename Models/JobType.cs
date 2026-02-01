namespace ShiftManager.Models;

public class JobType
{
    public int Id { get; set; }
    public int AreaId { get; set; }  // Area-scoped (same JobTypes across molecules in an area)
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Color { get; set; }  // For UI display (hex color code)
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Area Area { get; set; } = null!;
    public List<AppUser> Users { get; set; } = new();
}
