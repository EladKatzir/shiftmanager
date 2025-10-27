namespace ShiftManager.Models;

public class ShiftAssignment : IBelongsToCompany
{
    public int Id { get; set; }

    // Multitenancy Phase 1: Tenant scoping
    public int CompanyId { get; set; }

    public int ShiftInstanceId { get; set; }
    public ShiftInstance ShiftInstance { get; set; } = null!;

    // Nullable to support unassigned slots (roster grid feature)
    public int? UserId { get; set; }
    public AppUser? User { get; set; }

    // Trainee shadowing support
    public int? TraineeUserId { get; set; }
    public AppUser? Trainee { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
