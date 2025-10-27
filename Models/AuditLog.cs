namespace ShiftManager.Models;

/// <summary>
/// Audit log entry for tracking all significant actions in the system.
/// Provides compliance, debugging, and security auditing capabilities.
/// </summary>
public class AuditLog : IBelongsToCompany
{
    /// <summary>
    /// Primary key
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Company this audit log belongs to (multi-tenant isolation)
    /// </summary>
    public int CompanyId { get; set; }

    /// <summary>
    /// User who performed the action (null for system actions)
    /// </summary>
    public int? UserId { get; set; }

    /// <summary>
    /// User's email at time of action (denormalized for deleted users)
    /// </summary>
    public string UserEmail { get; set; } = string.Empty;

    /// <summary>
    /// User's display name at time of action (denormalized for deleted users)
    /// </summary>
    public string UserDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Action performed (e.g., "ShiftAssigned", "UserCreated", "TimeOffApproved")
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Type of entity affected (e.g., "ShiftAssignment", "User", "TimeOffRequest")
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// ID of the affected entity (nullable for actions without specific entity)
    /// </summary>
    public int? EntityId { get; set; }

    /// <summary>
    /// Human-readable description of the action
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Additional details in JSON format (optional, for before/after state)
    /// </summary>
    public string? Details { get; set; }

    /// <summary>
    /// When the action occurred (UTC)
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// IP address of the user who performed the action
    /// </summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>
    /// User agent (browser info) of the user who performed the action
    /// </summary>
    public string UserAgent { get; set; } = string.Empty;

    // Navigation properties
    public Company? Company { get; set; }
    public AppUser? User { get; set; }
}
