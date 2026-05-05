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

    /// <summary>
    /// Per-assignment trainee flag (Shikma model: trainees take shifts directly, not shadowing).
    /// Mutually exclusive with TraineeUserId — set one or the other, not both.
    /// </summary>
    public bool IsTraineeShift { get; set; } = false;

    /// <summary>
    /// Cell-level annotation for display in calendar (e.g., "עולה ב13", "רחב").
    /// </summary>
    [System.ComponentModel.DataAnnotations.MaxLength(500)]
    public string? Note { get; set; }

    /// <summary>
    /// Links this shift assignment to its source TimeOffRequest (vacation/after).
    /// Set when materializing vacation or after shifts from approved requests.
    /// Used for diff-and-sync when the request is canceled or dates change.
    /// </summary>
    public int? SourceTimeOffRequestId { get; set; }
    public TimeOffRequest? SourceTimeOffRequest { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Row version for optimistic concurrency control.
    /// Automatically managed by SQL Server - do not modify manually.
    /// NOTE: [Timestamp] is a no-op on SQLite (no rowversion support). Kept for SQL Server migration compatibility.
    /// </summary>
    [System.ComponentModel.DataAnnotations.Timestamp]
    public byte[]? RowVersion { get; set; }
}
