using ShiftManager.Models.Support;

namespace ShiftManager.Models;

/// <summary>
/// Represents a chore assignment to a user on a specific date.
/// Chores are mutually exclusive with shift assignments (user cannot have both on same day).
/// </summary>
public class Chore : IBelongsToCompany
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    /// <summary>
    /// The molecule this chore belongs to (for scoping).
    /// Nullable for legacy chores created before molecule scoping was added.
    /// </summary>
    public int? MoleculeId { get; set; }

    /// <summary>
    /// The user assigned to this chore
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// Date of the chore (stored as DateOnly, no time component)
    /// </summary>
    public DateOnly Date { get; set; }

    /// <summary>
    /// Short title/description of the chore (required)
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Optional additional notes/details
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// User who created/assigned this chore
    /// </summary>
    public int CreatedBy { get; set; }

    /// <summary>
    /// When the chore was created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the chore was canceled (null = active, not null = canceled/soft deleted)
    /// </summary>
    public DateTime? CanceledAt { get; set; }

    /// <summary>
    /// User who canceled this chore (null if not canceled)
    /// </summary>
    public int? CanceledBy { get; set; }

    // Navigation properties
    public Company? Company { get; set; }
    public Molecule? Molecule { get; set; }
    public AppUser? User { get; set; }
    public AppUser? Creator { get; set; }
    public AppUser? Canceler { get; set; }

    /// <summary>
    /// Whether this chore is active (not canceled)
    /// </summary>
    public bool IsActive => CanceledAt == null;
}
