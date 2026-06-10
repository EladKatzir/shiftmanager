using ShiftManager.Models.Support;

namespace ShiftManager.Services.Notifications;

/// <summary>
/// Pure decision logic for the notification dispatch layer (no I/O), kept separate so it can be
/// exhaustively unit-tested. Encodes the two rules from the design spec (§5.3, §5.6):
///   1. the email precedence chain, and
///   2. the 20-unread "catch up" throttle.
/// </summary>
public static class NotificationGate
{
    /// <summary>Unread in-app count at which a Quiet-mode user gets one "catch up" email.</summary>
    public const int CatchUpThreshold = 20;

    /// <summary>
    /// Email precedence chain (in-app is always persisted regardless of this result):
    /// <code>
    /// if SecurityCritical            -> EMAIL   (ignores mode AND mutes)
    /// else if category muted         -> no email
    /// else if Quiet && !Actionable   -> no email (relies on the catch-up throttle)
    /// else                           -> EMAIL
    /// </code>
    /// </summary>
    public static bool ShouldSendEmail(
        bool securityCritical,
        bool personallyActionable,
        EngagementMode mode,
        bool categoryMuted)
    {
        if (securityCritical) return true;
        if (categoryMuted) return false;
        if (mode == EngagementMode.Quiet && !personallyActionable) return false;
        return true;
    }

    /// <summary>
    /// Throttle rule: a Quiet-mode user gets exactly ONE "catch up" email when their unread
    /// in-app count crosses the threshold. <paramref name="catchUpPending"/> is the guard that
    /// prevents re-firing within the same accumulation cycle; it is reset (see
    /// <see cref="ShouldResetCatchUpGuard"/>) once the user reads notifications and drops below
    /// the threshold.
    /// </summary>
    public static bool ShouldSendCatchUp(
        EngagementMode mode,
        int unreadCount,
        bool catchUpPending)
    {
        return mode == EngagementMode.Quiet
            && unreadCount >= CatchUpThreshold
            && !catchUpPending;
    }

    /// <summary>
    /// True when the catch-up guard should be cleared: the user has read enough that their unread
    /// count is back below the threshold, so the next accumulation cycle can fire again.
    /// </summary>
    public static bool ShouldResetCatchUpGuard(int unreadCount, bool catchUpPending)
    {
        return catchUpPending && unreadCount < CatchUpThreshold;
    }
}
