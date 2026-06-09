using System.Threading.Tasks;

namespace ShiftManager.Services.Notifications;

/// <summary>
/// Single entry point for raising notification events. Implementations fan an event out
/// to the in-app and email channels (and later calendar). Fire-and-forget by contract:
/// a notification failure must never throw into the calling business action.
/// </summary>
public interface INotificationDispatcher
{
    /// <summary>
    /// Raise a notification event. In Phase 0 this delegates to the existing
    /// INotificationService creators. Unmapped event types are logged and skipped
    /// (never thrown) so a missing mapping cannot break a business action.
    /// </summary>
    Task RaiseAsync(NotificationEvent evt);
}
