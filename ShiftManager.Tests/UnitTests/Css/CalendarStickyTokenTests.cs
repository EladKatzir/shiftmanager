using System.Text.RegularExpressions;
using FluentAssertions;

namespace ShiftManager.Tests.UnitTests.Css;

/// <summary>
/// Locks the z-index tier convention for the calendar sticky-headers feature
/// (spec §6). Any future edit that breaks the ordering — e.g. setting
/// --z-sticky-col higher than --z-sticky-header — fails here.
/// </summary>
public class CalendarStickyTokenTests
{
    private static readonly Regex TokenRegex =
        new(@"--(?<name>[a-z0-9\-]+)\s*:\s*(?<value>[^;]+);", RegexOptions.Compiled);

    private static Dictionary<string, string> LoadTokens()
    {
        var repoRoot = LocateRepoRoot();
        var path = Path.Combine(repoRoot, "wwwroot", "css", "tokens.css");
        File.Exists(path).Should().BeTrue($"tokens.css must exist at {path}");

        var css = File.ReadAllText(path);
        return TokenRegex.Matches(css)
            .Cast<Match>()
            .GroupBy(m => m.Groups["name"].Value)
            .ToDictionary(g => g.Key, g => g.First().Groups["value"].Value.Trim());
    }

    private static int ParseZ(string raw) =>
        int.Parse(raw.Split(' ', '/', '*')[0].TrimEnd(';').Trim());

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ShiftManager.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("ShiftManager.csproj not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void StickyTierTokens_AreDefinedAndOrderedCorrectly()
    {
        var tokens = LoadTokens();

        tokens.Should().ContainKey("z-sticky-col");
        tokens.Should().ContainKey("z-sticky");
        tokens.Should().ContainKey("z-sticky-group");
        tokens.Should().ContainKey("z-sticky-header");
        tokens.Should().ContainKey("z-sticky-corner");

        var col    = int.Parse(tokens["z-sticky-col"]);
        var sticky = int.Parse(tokens["z-sticky"]);
        var group  = int.Parse(tokens["z-sticky-group"]);
        var header = int.Parse(tokens["z-sticky-header"]);
        var corner = int.Parse(tokens["z-sticky-corner"]);

        col.Should().BeLessThan(sticky, "sticky col is below default sticky / toolbar");
        sticky.Should().BeLessThan(group, "default sticky is below group bands");
        group.Should().BeLessThan(header, "group bands are below the table header");
        header.Should().BeLessThan(corner, "table header is below the corner cell");
    }

    [Fact]
    public void StickyShadowTokens_ArePresent()
    {
        var tokens = LoadTokens();
        tokens.Should().ContainKey("shadow-sticky-inline");
        tokens.Should().ContainKey("shadow-sticky-inline-rtl");
        tokens.Should().ContainKey("shadow-sticky-block");
    }

    [Fact]
    public void ExcelCalendarHeaderHeight_TokenIsPresent()
    {
        var tokens = LoadTokens();
        tokens.Should().ContainKey("excel-calendar-header-height");
    }

    [Fact]
    public void DropdownZIndex_IsAboveStickyCorner()
    {
        var tokens = LoadTokens();
        tokens.Should().ContainKey("z-dropdown");
        tokens.Should().ContainKey("z-sticky-corner");

        var dropdown = int.Parse(tokens["z-dropdown"]);
        var corner   = int.Parse(tokens["z-sticky-corner"]);

        dropdown.Should().BeGreaterThanOrEqualTo(corner,
            "dropdown menus must paint above the sticky corner — " +
            "otherwise the DL dropdown opens behind the sticky thead when " +
            "the toolbar pins. This was a latent bug fixed alongside the " +
            "sticky toolbar in the 2026-05-29 calendar-sticky-headers PR.");
    }
}
