namespace ShiftManager.Models.ViewModels;

/// <summary>
/// View model for the Decision Ribbon component.
/// Shows pending approval count and user role for determining visibility and permissions.
/// </summary>
public class DecisionRibbonViewModel
{
    /// <summary>
    /// Total count of pending requests requiring approval.
    /// For Managers: time-off requests only.
    /// For Directors/Owners: time-off + swap requests.
    /// </summary>
    public int PendingCount { get; set; }

    /// <summary>
    /// User's role (Manager, Director, Owner).
    /// Used for role-specific messaging and actions.
    /// </summary>
    public string UserRole { get; set; } = string.Empty;
}
