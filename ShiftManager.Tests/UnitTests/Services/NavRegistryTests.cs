using System.Collections.Generic;
using System.Linq;
using ShiftManager.Data.SeedData;
using ShiftManager.Models.Navigation;
using ShiftManager.Services.Navigation;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Structural guarantees for the navigation tree. The grant-key parity test is the cheap half of
/// P2: every "Grant:X" a nav node declares must reference a grant that actually exists, so a typo
/// can never produce a link that authorizes against a non-existent (always-deny) policy.
/// </summary>
public class NavRegistryTests
{
    private static IEnumerable<NavNode> Flatten(IEnumerable<NavNode> nodes)
    {
        foreach (var n in nodes)
        {
            yield return n;
            foreach (var c in Flatten(n.ChildNodes)) yield return c;
        }
    }

    [Fact]
    public void AllDomainHubs_ArePresent()
    {
        var top = NavRegistry.Root.Select(n => n.LocKey).ToHashSet();
        foreach (var hub in new[] { "Nav2_Home", "Nav2_Scheduling", "Requests", "Nav2_People",
                                    "Organization", "Nav2_Access", "Nav2_Insights", "Nav2_System", "Nav_Personal" })
        {
            Assert.Contains(hub, top);
        }
    }

    [Fact]
    public void Scheduling_HasVerbFirstTabs()
    {
        var scheduling = NavRegistry.Root.Single(n => n.LocKey == "Nav2_Scheduling");
        var childKeys = scheduling.ChildNodes.Select(c => c.LocKey).ToHashSet();
        Assert.Contains("Nav2_Sched_Calendars", childKeys);
        Assert.Contains("Nav2_Sched_Planning", childKeys);
        Assert.Contains("Nav2_Sched_Definitions", childKeys);
        Assert.Contains("Nav2_Eligibility", childKeys);
        Assert.Contains("Nav2_Rules", childKeys);
    }

    [Fact]
    public void NoDuplicateRoutes()
    {
        var routes = Flatten(NavRegistry.Root).Where(n => n.Route != null).Select(n => n.Route!).ToList();
        var dupes = routes.GroupBy(r => r).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dupes.Count == 0, "Duplicate routes in NavRegistry: " + string.Join(", ", dupes));
    }

    [Fact]
    public void EveryNonGroupNode_HasARoute()
    {
        foreach (var n in Flatten(NavRegistry.Root))
        {
            if (!n.IsGroup)
                Assert.False(string.IsNullOrEmpty(n.Route), $"Node '{n.LocKey}' is a leaf but has no route.");
        }
    }

    [Fact]
    public void EveryGrantPolicy_ReferencesAnExistingGrant()
    {
        var validGrantKeys = GrantTypeSeed.GetGrantTypes().Select(g => g.Key).ToHashSet();

        foreach (var n in Flatten(NavRegistry.Root))
        {
            if (n.Policy is null) continue;
            Assert.StartsWith("Grant:", n.Policy);
            var grantKey = n.Policy.Substring("Grant:".Length);
            Assert.True(validGrantKeys.Contains(grantKey),
                $"Nav node '{n.LocKey}' (route {n.Route}) references unknown grant '{grantKey}'.");
        }
    }
}
