namespace ShiftManager.Models;

public class AreaSettings
{
    public int Id { get; set; }
    public int AreaId { get; set; }
    public int DefaultRestHours { get; set; } = 11;
    public int DefaultWeeklyCap { get; set; } = 60;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int? UpdatedByUserId { get; set; }

    // Navigation
    public Area Area { get; set; } = null!;
    public AppUser? UpdatedByUser { get; set; }
}
