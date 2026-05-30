using System.Text.RegularExpressions;
using FluentAssertions;

namespace ShiftManager.Tests.UnitTests.Css;

/// <summary>
/// Locks RTL discipline for the .excel-calendar* and .cal-* selectors in
/// calendar.css. Any new `left:` or `right:` declaration inside one of these
/// rules fails here — the codebase contract is to use logical properties
/// (inset-inline-start, inset-inline-end) so Hebrew mode pins the correct
/// logical side automatically.
///
/// Exception: explicit `[dir="rtl"]` overrides for box-shadow are ALLOWED
/// (box-shadow is a physical property; no logical equivalent exists).
/// The regex below excludes those.
/// </summary>
public class CalendarStickyRtlSweepTests
{
    private static readonly string[] GuardedSelectorPrefixes = new[]
    {
        ".excel-calendar",
        ".cal-toolbar",
        ".cal-page",
    };

    private static readonly Regex RuleRegex =
        new(@"(?<selector>[^{};]+?)\s*\{(?<body>[^{}]*)\}", RegexOptions.Compiled);

    private static readonly Regex PhysicalSideRegex =
        new(@"(?<![\w-])(left|right)\s*:\s*[^;]+;", RegexOptions.Compiled);

    [Fact]
    public void NoPhysicalLeftRightInCalendarStickySelectors()
    {
        var repoRoot = LocateRepoRoot();
        var path = Path.Combine(repoRoot, "wwwroot", "css", "calendar.css");
        var css = File.ReadAllText(path);

        var offenders = new List<string>();
        foreach (Match rule in RuleRegex.Matches(css))
        {
            var selector = rule.Groups["selector"].Value.Trim();
            var body = rule.Groups["body"].Value;

            // Skip if selector doesn't touch our guarded prefixes.
            if (!GuardedSelectorPrefixes.Any(p => selector.Contains(p)))
                continue;

            // Skip `[dir="rtl"]` overrides — they're allowed to use physical
            // properties because they're explicitly redeclaring for RTL.
            if (selector.Contains("[dir=\"rtl\"]") || selector.Contains("[dir='rtl']"))
                continue;

            foreach (Match m in PhysicalSideRegex.Matches(body))
            {
                offenders.Add($"selector `{selector}` contains `{m.Value.Trim()}`");
            }
        }

        offenders.Should().BeEmpty(
            "calendar.css must use inset-inline-start / inset-inline-end " +
            "instead of left/right inside .excel-calendar / .cal-toolbar / " +
            ".cal-page rules so RTL (Hebrew) mode pins the correct logical side. " +
            "If you genuinely need a physical override, wrap it in a " +
            "[dir=\"rtl\"] selector.");
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ShiftManager.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("ShiftManager.csproj not found");
    }
}
