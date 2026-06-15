namespace ShiftManager.Models;

/// <summary>
/// A per-person negative override: this user is waived from a specific chore type (e.g. a
/// disability accommodation). NOT an EligibilityRule (those are positive requirements on the type).
/// Reason is optional, sensitive, capped at 200 chars; never logged in plaintext audit.
/// </summary>
public class UserChoreExemption
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int ChoreTypeId { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }

    public AppUser User { get; set; } = null!;
    public ChoreType ChoreType { get; set; } = null!;
    public AppUser? Creator { get; set; }
}
