using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using ShiftManager.Services.Notifications;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// NLT-01 round-trip · NLT-02 tampered/garbage/null rejected · NLT-03 a different key cannot read it.
/// </summary>
public class NotificationLinkTokenServiceTests
{
    private static NotificationLinkTokenService NewService()
        => new(new EphemeralDataProtectionProvider());

    [Fact] // NLT-01
    public void RoundTrips()
    {
        var svc = NewService();
        var token = svc.CreateQuietToken(42);

        svc.TryParse(token, out var userId).Should().BeTrue();
        userId.Should().Be(42);
    }

    [Theory] // NLT-02
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    public void Rejects_MissingOrGarbage(string? token)
    {
        var svc = NewService();
        svc.TryParse(token, out var u).Should().BeFalse();
        u.Should().Be(0);
    }

    [Fact] // NLT-02b
    public void Rejects_Tampered()
    {
        var svc = NewService();
        var token = svc.CreateQuietToken(42);
        svc.TryParse(token + "AAAA", out _).Should().BeFalse();
    }

    [Fact] // NLT-03
    public void DifferentKey_CannotRead()
    {
        var a = NewService();
        var b = NewService(); // different ephemeral key
        var token = a.CreateQuietToken(42);
        b.TryParse(token, out _).Should().BeFalse();
    }
}
