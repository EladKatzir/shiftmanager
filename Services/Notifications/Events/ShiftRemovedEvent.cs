namespace ShiftManager.Services.Notifications.Events;

/// <summary>
/// Raised when a user is unassigned from a shift. Maps to
/// INotificationService.CreateShiftRemovedNotificationAsync (in-app + email today).
/// </summary>
public sealed record ShiftRemovedEvent : NotificationEvent
{
    public required int RecipientUserId { get; init; }
    public required string ShiftTypeName { get; init; }
    public required DateOnly ShiftDate { get; init; }
    public required TimeOnly StartTime { get; init; }
    public required TimeOnly EndTime { get; init; }

    public override NotificationCategory Category => NotificationCategory.ShiftAssignment;
    public override bool PersonallyActionable => true;
    public override bool CalendarEligible => true;
    public override IcsMethod? Ics => IcsMethod.Cancel;
    public override bool Batchable => true;
}
