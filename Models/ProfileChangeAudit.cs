namespace ShiftManager.Models;

/// <summary>
/// Audit trail for employee profile changes
/// </summary>
public class ProfileChangeAudit : IBelongsToCompany
{
    public int Id { get; set; }
    public int CompanyId { get; set; }

    /// <summary>
    /// User whose profile was changed
    /// </summary>
    public int TargetUserId { get; set; }

    /// <summary>
    /// User who made the change
    /// </summary>
    public int ChangedBy { get; set; }

    /// <summary>
    /// When the change occurred (UTC)
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Which field was changed (e.g., "Phone", "Department")
    /// </summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// Previous value (nullable for new profiles)
    /// </summary>
    public string? OldValue { get; set; }

    /// <summary>
    /// New value (nullable for deletions)
    /// </summary>
    public string? NewValue { get; set; }

    // Navigation properties
    public AppUser? TargetUser { get; set; }
    public AppUser? ChangedByUser { get; set; }
    public Company? Company { get; set; }
}
