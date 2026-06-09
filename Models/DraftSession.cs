namespace ShiftManager.Models;

public enum DraftSessionStatus
{
    Active = 0,
    Committed = 1,
    Discarded = 2
}

/// <summary>
/// A private, per-assigner sandbox over one molecule+week of the shift calendar (Epic 4 — Draft Mode).
/// Edits made while a session is Active are staged in <see cref="DraftCell"/> rows and never touch the
/// live board or fire SignalR; committing reconciles only the touched cells into the live schedule with
/// optimistic, per-cell conflict detection. Molecule-scoped (no tenant filter) so an assigner can stage
/// changes across the companies of their molecule, exactly like the live calendar.
/// </summary>
public class DraftSession
{
    public int Id { get; set; }
    public int OwnerUserId { get; set; }
    public int MoleculeId { get; set; }
    public int? JobTypeId { get; set; }
    public DateOnly WeekStart { get; set; }
    public DateOnly WeekEnd { get; set; }
    public DraftSessionStatus Status { get; set; } = DraftSessionStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public AppUser Owner { get; set; } = null!;
    public List<DraftCell> Cells { get; set; } = new();
}
