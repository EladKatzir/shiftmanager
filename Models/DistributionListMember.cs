namespace ShiftManager.Models;

/// <summary>
/// Many-to-many join between a <see cref="DistributionList"/> and a member <see cref="AppUser"/>.
/// Does NOT implement IBelongsToCompany — membership is scoped through its parent list (and members may
/// span multiple companies within the molecule). Deleted by cascade when the parent list is deleted.
/// </summary>
public class DistributionListMember
{
    public int Id { get; set; }

    /// <summary>
    /// The list this membership belongs to.
    /// </summary>
    public int DistributionListId { get; set; }

    /// <summary>
    /// The member user (any active user within the list's molecule, across companies).
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// When this member was added to the list.
    /// </summary>
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public DistributionList DistributionList { get; set; } = null!;
    public AppUser User { get; set; } = null!;
}
