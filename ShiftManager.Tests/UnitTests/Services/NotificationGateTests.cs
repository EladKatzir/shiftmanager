using FluentAssertions;
using ShiftManager.Models.Support;
using ShiftManager.Services.Notifications;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Pure decision-logic tests for the notification gate (§5.3 email precedence, §5.6 throttle).
/// NG-E*: ShouldSendEmail precedence. NG-T*: catch-up throttle + reset.
/// </summary>
public class NotificationGateTests
{
    // ───────────────────────── Email precedence ─────────────────────────

    [Fact] // NG-E1: security-critical always emails, even in Quiet + muted
    public void SecurityCritical_AlwaysEmails()
    {
        NotificationGate.ShouldSendEmail(
            securityCritical: true, personallyActionable: false,
            mode: EngagementMode.Quiet, categoryMuted: true)
            .Should().BeTrue();
    }

    [Fact] // NG-E2: muted category suppresses email (when not security-critical)
    public void Muted_Suppresses()
    {
        NotificationGate.ShouldSendEmail(
            securityCritical: false, personallyActionable: true,
            mode: EngagementMode.Engaged, categoryMuted: true)
            .Should().BeFalse();
    }

    [Fact] // NG-E3: Quiet + non-actionable suppresses email
    public void Quiet_NonActionable_Suppresses()
    {
        NotificationGate.ShouldSendEmail(
            securityCritical: false, personallyActionable: false,
            mode: EngagementMode.Quiet, categoryMuted: false)
            .Should().BeFalse();
    }

    [Fact] // NG-E4: Quiet + actionable still emails
    public void Quiet_Actionable_Emails()
    {
        NotificationGate.ShouldSendEmail(
            securityCritical: false, personallyActionable: true,
            mode: EngagementMode.Quiet, categoryMuted: false)
            .Should().BeTrue();
    }

    [Fact] // NG-E5: Engaged + non-actionable still emails (default = both channels everything)
    public void Engaged_NonActionable_Emails()
    {
        NotificationGate.ShouldSendEmail(
            securityCritical: false, personallyActionable: false,
            mode: EngagementMode.Engaged, categoryMuted: false)
            .Should().BeTrue();
    }

    // ───────────────────────── Catch-up throttle ─────────────────────────

    [Fact] // NG-T1: crossing threshold in Quiet with no pending guard → send once
    public void CatchUp_FiresAtThreshold()
    {
        NotificationGate.ShouldSendCatchUp(EngagementMode.Quiet, unreadCount: 20, catchUpPending: false)
            .Should().BeTrue();
    }

    [Fact] // NG-T2: guard already set → do not re-fire
    public void CatchUp_DoesNotRefireWhilePending()
    {
        NotificationGate.ShouldSendCatchUp(EngagementMode.Quiet, unreadCount: 35, catchUpPending: true)
            .Should().BeFalse();
    }

    [Fact] // NG-T3: below threshold → no catch-up
    public void CatchUp_BelowThreshold_DoesNotFire()
    {
        NotificationGate.ShouldSendCatchUp(EngagementMode.Quiet, unreadCount: 19, catchUpPending: false)
            .Should().BeFalse();
    }

    [Fact] // NG-T4: Engaged users never get the catch-up (they already get every email)
    public void CatchUp_EngagedNeverFires()
    {
        NotificationGate.ShouldSendCatchUp(EngagementMode.Engaged, unreadCount: 50, catchUpPending: false)
            .Should().BeFalse();
    }

    [Fact] // NG-T5: guard resets once unread drops below threshold
    public void Reset_WhenBelowThresholdAndPending()
    {
        NotificationGate.ShouldResetCatchUpGuard(unreadCount: 5, catchUpPending: true).Should().BeTrue();
    }

    [Fact] // NG-T6: no reset when still at/above threshold, or when not pending
    public void Reset_NotWhenStillHighOrNotPending()
    {
        NotificationGate.ShouldResetCatchUpGuard(unreadCount: 25, catchUpPending: true).Should().BeFalse();
        NotificationGate.ShouldResetCatchUpGuard(unreadCount: 0, catchUpPending: false).Should().BeFalse();
    }
}
