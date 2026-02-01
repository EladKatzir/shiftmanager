using ShiftManager.Models.Support;

namespace ShiftManager.Models;

/// <summary>
/// "Day Shift" assignment (user-facing term: "משמרות יומיות" / "Day Shifts").
///
/// TERMINOLOGY NOTE: Class named "OnDuty" for historical reasons.
/// In the UI, these are presented as "Day Shifts" to be inclusive of all workers.
///
/// Characteristics:
/// - Global (not company-scoped), supports cross-company assignments
/// - Full day-length (no specific start/end times, just dates)
/// - One person per type per date
///
/// See TERMINOLOGY.md for complete terminology mapping.
/// </summary>
public class OnDuty
{
    public int Id { get; set; }

    /// <summary>
    /// User assigned to on-duty (can be from any company)
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// Date of the on-duty assignment
    /// </summary>
    public DateOnly Date { get; set; }

    /// <summary>
    /// Type of on-duty: Hakam or Lead
    /// </summary>
    public OnDutyType Type { get; set; }

    /// <summary>
    /// Optional notes about this assignment
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Who created this assignment
    /// </summary>
    public int CreatedBy { get; set; }

    /// <summary>
    /// When this assignment was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// If canceled, when was it canceled
    /// </summary>
    public DateTime? CanceledAt { get; set; }

    /// <summary>
    /// If canceled, who canceled it
    /// </summary>
    public int? CanceledBy { get; set; }

    /// <summary>
    /// Navigation property to the assigned user
    /// </summary>
    public AppUser? User { get; set; }

    /// <summary>
    /// Navigation property to the creator
    /// </summary>
    public AppUser? Creator { get; set; }

    /// <summary>
    /// Navigation property to the canceler (if canceled)
    /// </summary>
    public AppUser? Canceler { get; set; }

    /// <summary>
    /// Check if this on-duty assignment is active (not canceled)
    /// </summary>
    public bool IsActive => CanceledAt == null;
}
