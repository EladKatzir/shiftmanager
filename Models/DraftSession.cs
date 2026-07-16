namespace ShiftManager.Models;

public enum DraftSessionStatus
{
    Active = 0,
    Committed = 1,
    Discarded = 2
}

/// <summary>
/// Which calendar surface a <see cref="DraftSession"/> sandboxes. Draft Mode spans all three calendars
/// (Foundation Spec F); each surface has its own cell table + reconciler but shares this session + the
/// lifecycle/commit engine. Persisted as an int — DO NOT renumber (existing rows default to 0=Shifts).
/// </summary>
public enum DraftSurface
{
    Shifts = 0,
    Chores = 1,
    OnCall = 2
}

/// <summary>
/// A private, per-assigner sandbox over one calendar surface + scope + week (Epic 4 — Draft Mode).
/// Edits made while a session is Active are staged in per-surface cell rows (<see cref="DraftCell"/> for
/// shifts, <see cref="DraftChoreCell"/> for chores, <see cref="DraftDutyCell"/> for on-call) and never
/// touch the live board or fire SignalR; committing reconciles only the touched cells into the live
/// schedule with per-cell conflict detection + per-cell re-authorization. Scoped without a tenant filter
/// so an assigner can stage across the companies of their molecule (or the global on-call space), exactly
/// like the live calendar.
///
/// Scope tuple per surface (the "one active draft per …" key, enforced by filtered-unique indexes):
///  - Shifts: (OwnerUserId, Surface, MoleculeId, JobTypeId, WeekStart)
///  - Chores: (OwnerUserId, Surface, MoleculeId, WeekStart)              (JobTypeId null)
///  - OnCall: (OwnerUserId, Surface, AreaId, WeekStart)                   (MoleculeId null; AreaId is a UI hint only)
/// </summary>
public class DraftSession
{
    public int Id { get; set; }
    public int OwnerUserId { get; set; }

    /// <summary>Which calendar surface this sandbox covers.</summary>
    public DraftSurface Surface { get; set; } = DraftSurface.Shifts;

    /// <summary>Shifts/chores scope. Null for on-call (which is global, keyed by <see cref="AreaId"/> for the UI only).</summary>
    public int? MoleculeId { get; set; }

    /// <summary>Shifts-only scope axis. Null for chores/on-call.</summary>
    public int? JobTypeId { get; set; }

    /// <summary>On-call presentation hint (which area's rows/pickers to show). NEVER a data boundary — on-call is global. Null for shifts/chores.</summary>
    public int? AreaId { get; set; }

    public DateOnly WeekStart { get; set; }
    public DateOnly WeekEnd { get; set; }
    public DraftSessionStatus Status { get; set; } = DraftSessionStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public AppUser Owner { get; set; } = null!;
    public List<DraftCell> Cells { get; set; } = new();
    public List<DraftChoreCell> ChoreCells { get; set; } = new();
    public List<DraftDutyCell> DutyCells { get; set; } = new();
}
