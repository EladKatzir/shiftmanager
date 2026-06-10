using ShiftManager.Models.Support;
using ShiftManager.Services.Notifications;

namespace ShiftManager.Services;

/// <summary>
/// Owns per-user notification engagement preferences (engagement mode + per-category email mutes)
/// and the 20-unread "catch up" throttle state. All methods take explicit userId + companyId and
/// look up across tenants, so they work both inside an HTTP request and from the anonymous,
/// token-authenticated opt-out endpoint (which has no tenant context).
/// </summary>
public interface INotificationPreferenceService
{
    /// <summary>Current engagement mode (defaults to <see cref="EngagementMode.Engaged"/> when no row exists).</summary>
    Task<EngagementMode> GetEngagementModeAsync(int userId, int companyId);

    /// <summary>Set engagement mode, creating the preference row if necessary.</summary>
    Task SetEngagementModeAsync(int userId, int companyId, EngagementMode mode);

    /// <summary>The categories for which this user has muted EMAIL.</summary>
    Task<IReadOnlySet<NotificationCategory>> GetMutedCategoriesAsync(int userId, int companyId);

    /// <summary>Mute or un-mute email for a single category (idempotent).</summary>
    Task SetCategoryMuteAsync(int userId, int companyId, NotificationCategory category, bool muted);

    /// <summary>
    /// Apply the email precedence chain (engagement mode + mutes + the event's metadata) to decide
    /// whether the email channel should fire for this event/recipient. In-app is always persisted
    /// regardless of this result.
    /// </summary>
    Task<bool> ShouldSendEmailAsync(int userId, int companyId, NotificationEvent evt);

    /// <summary>
    /// Raw-metadata overload of the email gate, for callers (the existing typed notification
    /// creators) that pass category/flags inline rather than constructing a NotificationEvent.
    /// </summary>
    Task<bool> ShouldSendEmailAsync(int userId, int companyId, NotificationCategory category, bool personallyActionable, bool securityCritical);

    /// <summary>
    /// Throttle evaluation, called right after an in-app notification is persisted. Returns true if
    /// the caller should send ONE "catch up" email now (Quiet mode + unread crossed the threshold +
    /// no catch-up already pending). When it returns true it has already set the pending guard +
    /// timestamp. It also self-heals the guard: if the user has read enough to drop below the
    /// threshold, the guard is cleared so a future accumulation cycle can fire again.
    /// </summary>
    Task<bool> TryBeginCatchUpAsync(int userId, int companyId, int unreadCount);
}
