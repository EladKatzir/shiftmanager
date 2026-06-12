namespace ShiftManager.Models;

/// <summary>
/// A single user's personal selection of shift types to monitor on the "Who is on Shift" home dashboard.
/// NOT IBelongsToCompany — it is a per-user preference read only for its owner,
/// so there is no cross-tenant read path.
/// </summary>
public class UserMonitoredShift
{
    public int Id { get; set; }

    /// <summary>Owner of this personal monitoring selection.</summary>
    public int UserId { get; set; }

    /// <summary>The shift type the user has chosen to monitor.</summary>
    public int ShiftTypeId { get; set; }
}
