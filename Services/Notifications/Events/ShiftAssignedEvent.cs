namespace ShiftManager.Services.Notifications.Events;

/// <summary>
/// Raised when a user is assigned to a shift. Maps to
/// INotificationService.CreateShiftAddedNotificationAsync (in-app + email today).
/// </summary>
public sealed record ShiftAssignedEvent : NotificationEvent
{
    public required int RecipientUserId { get; init; }
    public required string ShiftTypeName { get; init; }
    public required DateOnly ShiftDate { get; init; }
    public required TimeOnly StartTime { get; init; }
    public required TimeOnly EndTime { get; init; }

    public override NotificationCategory Category => NotificationCategory.ShiftAssignment;
    public override bool PersonallyActionable => true;
    public override bool CalendarEligible => true;
    public override IcsMethod? Ics => IcsMethod.Request;
    public override bool Batchable => true;
}
