namespace ShiftManager.Models;

public class CompanySettings
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int? RestHoursOverride { get; set; }
    public int? WeeklyCapOverride { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int? UpdatedByUserId { get; set; }

    // Navigation
    public Company Company { get; set; } = null!;
    public AppUser? UpdatedByUser { get; set; }
}
