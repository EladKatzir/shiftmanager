using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Moq;
using ShiftManager.Data.SeedData;
using ShiftManager.Models.Navigation;
using ShiftManager.Services;
using ShiftManager.Services.Navigation;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Exercises the real <see cref="NavRegistry"/> through <see cref="NavigationService"/> with a
/// mocked authorization service, proving the P2 "visibility == access" filtering: a node is
/// visible iff its declared policy authorizes (or it is policy-less), hubs collapse when empty,
/// flag-gated nodes hide when their flag is off, and the role-aware viewer sees "My Schedule".
/// </summary>
public class NavigationServiceTests
{
    private static ClaimsPrincipal AuthedUser() =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "1") }, "TestAuth"));

    private static NavigationService Build(
        bool authorizeAll,
        bool flagsOn,
        bool authenticated = true,
        System.Collections.Generic.HashSet<string>? grantPolicies = null)
    {
        var authz = new Mock<IAuthorizationService>();
        authz.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object?>(), It.IsAny<string>()))
            .ReturnsAsync((ClaimsPrincipal _, object? __, string policy) =>
                (authorizeAll || (grantPolicies?.Contains(policy) ?? false))
                    ? AuthorizationResult.Success()
                    : AuthorizationResult.Failed());

        var flags = new Mock<IFeatureFlagService>();
        flags.Setup(f => f.IsEnabled(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>())).Returns(flagsOn);

        var current = new Mock<ICurrentUserService>();
        current.SetupGet(c => c.IsAuthenticated).Returns(authenticated);
        current.SetupGet(c => c.UserId).Returns(1);
        current.SetupGet(c => c.CompanyId).Returns(1);

        return new NavigationService(authz.Object, flags.Object, current.Object);
    }

    [Fact]
    public void IsNewNavEnabled_False_WhenUnauthenticated()
    {
        var svc = Build(authorizeAll: true, flagsOn: true, authenticated: false);
        Assert.False(svc.IsNewNavEnabled());
    }

    [Fact]
    public void IsNewNavEnabled_True_WhenAuthenticatedAndFlagOn()
    {
        var svc = Build(authorizeAll: true, flagsOn: true);
        Assert.True(svc.IsNewNavEnabled());
    }

    [Fact]
    public void IsNewNavEnabled_False_WhenFlagOff()
    {
        var svc = Build(authorizeAll: true, flagsOn: false);
        Assert.False(svc.IsNewNavEnabled());
    }

    [Fact]
    public async Task Owner_SeesEveryHub()
    {
        var svc = Build(authorizeAll: true, flagsOn: true);
        var nav = await svc.GetVisibleNavAsync(AuthedUser());
        var keys = nav.Select(n => n.LocKey).ToHashSet();

        foreach (var hub in new[] { "Nav2_Home", "Nav2_Scheduling", "Requests", "Nav2_Approvals",
                                    "Section_DirectorTools", "Nav2_People", "Organization", "Nav2_Access",
                                    "Nav2_Insights", "Nav2_System", "Nav_Personal" })
        {
            Assert.Contains(hub, keys);
        }
    }

    [Fact]
    public async Task Employee_SeesOnlyUngatedSurfaces()
    {
        // No grant policies authorize; flags off (so Friends/DutyRotations/etc. hide too).
        var svc = Build(authorizeAll: false, flagsOn: false);
        var nav = await svc.GetVisibleNavAsync(AuthedUser());
        var keys = nav.Select(n => n.LocKey).ToHashSet();

        // Visible: policy-less surfaces.
        Assert.Contains("Nav2_Home", keys);
        Assert.Contains("Nav2_Scheduling", keys); // Calendars has policy-less Shifts + Overview
        Assert.Contains("Requests", keys);
        Assert.Contains("Nav_Personal", keys);

        // Hidden: every grant-gated hub.
        Assert.DoesNotContain("Nav2_Approvals", keys);
        Assert.DoesNotContain("Section_DirectorTools", keys);
        Assert.DoesNotContain("Nav2_People", keys);
        Assert.DoesNotContain("Organization", keys);
        Assert.DoesNotContain("Nav2_Access", keys);
        Assert.DoesNotContain("Nav2_Insights", keys);
        Assert.DoesNotContain("Nav2_System", keys);
    }

    [Fact]
    public async Task Employee_Scheduling_HasOnlyCalendars_ForViewerLabel()
    {
        var svc = Build(authorizeAll: false, flagsOn: false);
        var nav = await svc.GetVisibleNavAsync(AuthedUser());
        var scheduling = nav.Single(n => n.LocKey == "Nav2_Scheduling");

        // Only the Calendars subgroup survives → the renderer shows the "My Schedule" label.
        Assert.All(scheduling.ChildNodes,
            c => Assert.DoesNotContain(c.LocKey, new[] { "Nav2_Sched_Planning", "Nav2_Sched_Definitions", "Nav2_Eligibility", "Nav2_Rules" }));
        Assert.Contains(scheduling.ChildNodes, c => c.LocKey == "Nav2_Sched_Calendars");
    }

    [Fact]
    public async Task FlagGatedLeaf_HiddenWhenFlagOff_VisibleWhenOn()
    {
        // Friends leaf is gated by FF_FRIENDSHIPS_ENABLED; authorize everything else.
        var offNav = await Build(authorizeAll: true, flagsOn: false).GetVisibleLeavesAsync(AuthedUser());
        Assert.DoesNotContain(offNav, n => n.Route == "/Friends");

        var onNav = await Build(authorizeAll: true, flagsOn: true).GetVisibleLeavesAsync(AuthedUser());
        Assert.Contains(onNav, n => n.Route == "/Friends");
    }

    [Fact]
    public async Task GetVisibleLeaves_AllHaveRoutes()
    {
        var svc = Build(authorizeAll: true, flagsOn: true);
        var leaves = await svc.GetVisibleLeavesAsync(AuthedUser());
        Assert.NotEmpty(leaves);
        Assert.All(leaves, l => Assert.False(string.IsNullOrEmpty(l.Route)));
    }
}
