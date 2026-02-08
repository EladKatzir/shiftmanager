using ShiftManager.Models.Support;

namespace ShiftManager.Models;

public class DutyRotation : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public OnDutyType DutyType { get; set; }
    public RotationFrequency Frequency { get; set; } = RotationFrequency.Daily;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IncludeWeekends { get; set; } = false;
    public int MaxConsecutiveDays { get; set; } = 1;
    public int CurrentQueuePosition { get; set; } = 0;
    public DateOnly? LastAssignedDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Navigation
    public Company? Company { get; set; }
    public AppUser? Creator { get; set; }
    public List<DutyRotationEntry> Entries { get; set; } = new();
    public List<DutyRotationLog> Logs { get; set; } = new();
}
