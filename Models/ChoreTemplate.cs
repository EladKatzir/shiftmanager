namespace ShiftManager.Models;

/// <summary>
/// A reusable chore definition a manager can "stamp" across a date range. Carries NO schedule —
/// stamping (Phase 2) loops the existing manual CreateChoreAsync per (date, assignee). Molecule-scoped.
/// </summary>
public class ChoreTemplate
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? ChoreTypeId { get; set; }
    public string DefaultTitle { get; set; } = string.Empty;
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public int? WeightMinutesOverride { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }

    public Molecule Molecule { get; set; } = null!;
    public ChoreType? ChoreType { get; set; }
    public AppUser? Creator { get; set; }
}
