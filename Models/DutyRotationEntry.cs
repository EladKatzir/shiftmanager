namespace ShiftManager.Models;

public class DutyRotationEntry
{
    public int Id { get; set; }
    public int DutyRotationId { get; set; }
    public int UserId { get; set; }
    public int Position { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public DutyRotation? DutyRotation { get; set; }
    public AppUser? User { get; set; }
}
