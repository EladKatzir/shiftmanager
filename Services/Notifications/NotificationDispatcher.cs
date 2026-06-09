using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ShiftManager.Services.Notifications.Events;

namespace ShiftManager.Services.Notifications;

/// <summary>
/// Phase 0 dispatcher: delegates each event to the existing INotificationService creator
/// (which already persists in-app + sends email). No metadata is consumed yet — that
/// arrives in Phase 2. Unmapped events are logged and skipped, never thrown.
/// </summary>
public sealed partial class NotificationDispatcher : INotificationDispatcher
{
    private readonly INotificationService _notifications;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(INotificationService notifications, ILogger<NotificationDispatcher> logger)
    {
        _notifications = notifications;
        _logger = logger;
    }

    public async Task RaiseAsync(NotificationEvent evt)
    {
        switch (evt)
        {
            case ShiftAssignedEvent e:
                await _notifications.CreateShiftAddedNotificationAsync(
                    e.RecipientUserId, e.ShiftTypeName, e.ShiftDate, e.StartTime, e.EndTime);
                break;

            case ShiftRemovedEvent e:
                await _notifications.CreateShiftRemovedNotificationAsync(
                    e.RecipientUserId, e.ShiftTypeName, e.ShiftDate, e.StartTime, e.EndTime);
                break;

            default:
                LogNoMapping(_logger, evt.GetType().Name);
                break;
        }
    }

    // Source-generated LoggerMessage delegates for NotificationDispatcher.
    // EventId range 2100-2199 reserved for the notification dispatch layer.
    [LoggerMessage(EventId = 2100, Level = LogLevel.Warning,
        Message = "No dispatcher mapping for notification event {EventType}; skipped")]
    private static partial void LogNoMapping(ILogger logger, string eventType);
}
