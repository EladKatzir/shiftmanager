using ShiftManager.Models.Support;

namespace ShiftManager.Models;

public class TimeOffRequest : IBelongsToCompany
{
    public int Id { get; set; }

    // Multitenancy Phase 1: Tenant scoping
    public int CompanyId { get; set; }

    public int UserId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public TimeOffType Type { get; set; } = TimeOffType.Vacation;
    public string? Reason { get; set; }
    public RequestStatus Status { get; set; } = RequestStatus.Pending;

    /// <summary>
    /// Optional: Specific approver user ID (must be Manager, Director, or Owner).
    /// If null, any manager can approve.
    /// </summary>
    public int? ApproverId { get; set; }

    /// <summary>
    /// When true, this time-off request is visible only to the chosen approver (ApproverId).
    /// When false (default), it follows standard primary+fallback approver routing.
    /// </summary>
    public bool Private { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Get the actual start date and time when the time-off begins.
    /// Vacation: StartDate at 00:00
    /// After: StartDate at 16:00
    /// </summary>
    public DateTime GetActualStartDateTime()
    {
        return Type switch
        {
            TimeOffType.Vacation => StartDate.ToDateTime(TimeOnly.MinValue),
            TimeOffType.After => StartDate.ToDateTime(new TimeOnly(16, 0)),
            _ => StartDate.ToDateTime(TimeOnly.MinValue)
        };
    }

    /// <summary>
    /// Get the actual end date and time when the time-off ends.
    /// Vacation: EndDate+1 at 13:00
    /// After: StartDate+1 at 13:00
    /// </summary>
    public DateTime GetActualEndDateTime()
    {
        return Type switch
        {
            TimeOffType.Vacation => EndDate.AddDays(1).ToDateTime(new TimeOnly(13, 0)),
            TimeOffType.After => StartDate.AddDays(1).ToDateTime(new TimeOnly(13, 0)),
            _ => EndDate.ToDateTime(new TimeOnly(23, 59, 59))
        };
    }
}
