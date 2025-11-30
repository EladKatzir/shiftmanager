using ShiftManager.Models.Support;

namespace ShiftManager.Models;

/// <summary>
/// Subscription to receive "Today's On-Duty" notifications for specific role types
/// </summary>
public class OnDutyRoleSubscription : IBelongsToCompany
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    /// <summary>
    /// User who subscribed to this role type
    /// </summary>
    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    /// <summary>
    /// The OnDutyType value (0=Hakam, 1=Lead, or custom type value from OnDutyTypeConfig)
    /// </summary>
    public int OnDutyTypeValue { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether this subscription is active
    /// </summary>
    public bool IsActive { get; set; } = true;
}
