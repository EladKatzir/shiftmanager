namespace ShiftManager.Models;

/// <summary>
/// A single user's remembered tab selection per <c>(Molecule, JobType)</c> on the Shifts calendar. NOT
/// IBelongsToCompany — a per-user UI preference read only for its owner. <see cref="TabId"/> null =
/// no explicit tab (falls back to the synthetic "All" view). FK TabId → ShiftTab is SET NULL, so deleting a
/// tab reverts anyone's remembered preference. Unique per <c>(UserId, MoleculeId, JobTypeId)</c>.
/// </summary>
public class UserShiftTabPreference
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int MoleculeId { get; set; }
    public int? JobTypeId { get; set; }
    public int? TabId { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ShiftTab? Tab { get; set; }
}
