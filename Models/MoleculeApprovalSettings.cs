namespace ShiftManager.Models;

/// <summary>
/// Per-molecule approval configuration. Currently holds the threshold (in days) above which
/// a vacation requires dual approval. Editable only by users with MoleculeAdmin or Director
/// role-template scoped to that molecule.
/// </summary>
public class MoleculeApprovalSettings
{
    public int Id { get; set; }

    public int MoleculeId { get; set; }

    /// <summary>
    /// Vacations strictly longer than this number of days require approval from both tiers
    /// (Lead+Director for Alhut/Text; BRDirector+MoleculeAdmin otherwise). Default 7.
    /// After requests are always single-approval regardless of this value.
    /// </summary>
    public int DualApprovalDayThreshold { get; set; } = 7;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public int UpdatedByUserId { get; set; }

    public Molecule? Molecule { get; set; }
    public AppUser? UpdatedBy { get; set; }
}
