using ShiftManager.Models.Support;

namespace ShiftManager.Models;

public class DutyRotationLog
{
    public int Id { get; set; }
    public int DutyRotationId { get; set; }
    public int? AssignedUserId { get; set; }
    public int? SkippedUserId { get; set; }
    public string? SkipReason { get; set; }  // VACATION, CONFLICT, INACTIVE, RANK
    public DateOnly AssignmentDate { get; set; }
    public int? OnDutyId { get; set; }  // Links to created OnDuty record
    public bool WasAutoAssigned { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public DutyRotation? DutyRotation { get; set; }
    public AppUser? AssignedUser { get; set; }
    public AppUser? SkippedUser { get; set; }
    public OnDuty? OnDuty { get; set; }
}
