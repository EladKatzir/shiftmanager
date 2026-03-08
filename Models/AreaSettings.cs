namespace ShiftManager.Models;

public class AreaSettings
{
    public int Id { get; set; }
    public int AreaId { get; set; }
    public int DefaultRestHours { get; set; } = 8;
    public int DefaultWeeklyCap { get; set; } = 56;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int? UpdatedByUserId { get; set; }

    // Navigation
    public Area Area { get; set; } = null!;
    public AppUser? UpdatedByUser { get; set; }
}
