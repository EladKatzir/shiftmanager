using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Services.Navigation;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// The STRUCTURAL half of P2 (visibility == access): for every route-bearing nav node, the nav's
/// declared policy must NOT be looser than the destination page's own [Authorize(Policy=...)].
/// A looser nav policy is the bug class (user sees a link, then 403s). Equal-or-stricter is safe.
///
/// This reflects over the real PageModel types, so a node mislabeled with a wrong-but-real grant
/// (which <see cref="NavRegistryTests.EveryGrantPolicy_ReferencesAnExistingGrant"/> can't catch)
/// is caught here. This is the test the NavNode/NavRegistry comments refer to.
/// </summary>
public class NavRegistryPolicyParityTests
{
    private const string PagesRoot = "ShiftManager.Pages";

    private static string Norm(string r)
    {
        var q = r.IndexOf('?');
        if (q >= 0) r = r.Substring(0, q); // a "?view=..." route still addresses its base page
        return ("/" + r.Trim('/')).TrimEnd('/');
    }

    /// <summary>route (case-insensitive, no trailing slash) -> page policy ("Grant:X" or null = authenticated-only).</summary>
    private static Dictionary<string, string?> BuildPagePolicyMap()
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in typeof(NavRegistry).Assembly.GetTypes())
        {
            if (t.IsAbstract || !typeof(PageModel).IsAssignableFrom(t)) continue;
            if (t.Namespace is null) continue;
            if (!(t.Namespace == PagesRoot || t.Namespace.StartsWith(PagesRoot + ".", StringComparison.Ordinal))) continue;
            if (!t.Name.EndsWith("Model", StringComparison.Ordinal)) continue;

            var page = t.Name[..^"Model".Length];               // "Index", "Shifts", ...
            var folder = t.Namespace.Length > PagesRoot.Length
                ? t.Namespace[(PagesRoot.Length + 1)..].Replace('.', '/')
                : "";

            var policy = t.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                          .Cast<AuthorizeAttribute>()
                          .Select(a => a.Policy)
                          .FirstOrDefault(p => !string.IsNullOrEmpty(p)); // null if only [Authorize] or none

            if (page.Equals("Index", StringComparison.Ordinal))
            {
                map[Norm(folder)] = policy;                 // folder route
                map[Norm(folder + "/Index")] = policy;      // and its explicit /Index form
            }
            else
            {
                map[Norm((folder.Length > 0 ? folder + "/" : "") + page)] = policy;
            }
        }
        return map;
    }

    [Fact]
    public void EveryNavRoute_ResolvesToAPageModel_WithSafePolicy()
    {
        var pageMap = BuildPagePolicyMap();
        var unresolved = new List<string>();
        var risky = new List<string>();

        foreach (var leaf in NavRegistry.AllLeaves())
        {
            var route = Norm(leaf.Route!);
            if (!pageMap.TryGetValue(route, out var pagePolicy))
            {
                unresolved.Add($"{leaf.LocKey} -> {route}");
                continue;
            }

            // Safe iff the page is authenticated-only (anyone the nav lets through is authenticated),
            // OR the nav declares exactly the page's policy. Anything else risks a visible-but-403 link.
            var safe = pagePolicy is null || string.Equals(leaf.Policy, pagePolicy, StringComparison.Ordinal);
            if (!safe)
                risky.Add($"{leaf.LocKey} ({route}): nav='{leaf.Policy ?? "(none)"}' page='{pagePolicy}'");
        }

        Assert.True(unresolved.Count == 0, "Nav routes with no matching PageModel:\n  " + string.Join("\n  ", unresolved));
        Assert.True(risky.Count == 0, "Nav nodes looser than their page (visible-but-403 risk):\n  " + string.Join("\n  ", risky));
    }
}
