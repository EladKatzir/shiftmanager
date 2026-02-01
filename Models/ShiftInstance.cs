using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftManager.Models;

public class ShiftInstance
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public int ShiftTypeId { get; set; }
    public ShiftType ShiftType { get; set; } = null!;
    public DateOnly WorkDate { get; set; }
    public string Name { get; set; } = string.Empty; // Custom name for this specific shift instance

    public int StaffingRequired { get; set; } = 0;

    /// <summary>
    /// Legacy concurrency field (manually incremented).
    /// Kept for backward compatibility with existing code.
    /// </summary>
    [ConcurrencyCheck]
    public int Concurrency { get; set; } = 0;

    /// <summary>
    /// Row version for optimistic concurrency control (B-018).
    /// Automatically managed by SQL Server - do not modify manually.
    /// Used to detect concurrent edit conflicts.
    /// </summary>
    [Timestamp]
    public byte[]? RowVersion { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Indicates this instance has been detached from its original Program template.
    /// When true, shows "OVR" badge in UI and enables "Reset to Program" action.
    /// </summary>
    public bool IsDetached { get; set; } = false;

    /// <summary>
    /// Foreign key to the Program that generated this instance (null if manually created).
    /// Used for "Reset to Program" functionality to restore original settings.
    /// </summary>
    public int? OriginalProgramId { get; set; }

    /// <summary>
    /// JSON string tracking which fields were overridden (staffing, time, name).
    /// Parsed via ShiftInstanceOverride helper class.
    /// Example: {"Staffing": true, "Time": true, "Name": false}
    /// </summary>
    public string? OverriddenFields { get; set; }

    // Navigation property
    public ShiftProgram? OriginalProgram { get; set; }
}
