namespace ShiftManager.Services.Notifications;

/// <summary>
/// Base type for every notifiable domain event. Concrete events carry their payload
/// (recipient(s) + render data) and override the metadata that drives the dispatcher.
///
/// In Phase 0 the dispatcher only reads the concrete type (delegating to the existing
/// INotificationService creators). The metadata properties below are the stable contract
/// consumed later: PersonallyActionable + SecurityCritical drive Quiet-mode email rules
/// (Phase 2); CalendarEligible + Ics drive .ics generation (Phase 4–5); Batchable drives
/// bulk-op coalescing (Phase 3).
/// </summary>
public abstract record NotificationEvent
{
    /// <summary>Category for per-user mutes and digest sectioning.</summary>
    public abstract NotificationCategory Category { get; }

    /// <summary>True if this event still emails when the recipient is in Quiet mode.</summary>
    public abstract bool PersonallyActionable { get; }

    /// <summary>True if this event ALWAYS emails — ignores Quiet mode and category mutes.</summary>
    public virtual bool SecurityCritical => false;

    /// <summary>True if a calendar (.ics) artifact should accompany this event.</summary>
    public virtual bool CalendarEligible => false;

    /// <summary>iCalendar METHOD when <see cref="CalendarEligible"/>; otherwise null.</summary>
    public virtual IcsMethod? Ics => null;

    /// <summary>True if this event can be coalesced into one summary email during bulk ops.</summary>
    public virtual bool Batchable => false;
}
