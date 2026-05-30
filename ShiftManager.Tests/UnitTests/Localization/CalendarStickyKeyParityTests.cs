using System.Xml.Linq;
using FluentAssertions;

namespace ShiftManager.Tests.UnitTests.Localization;

/// <summary>
/// Locks EN ↔ HE parity for the 13 new Calendar_* keys introduced by the
/// 2026-05-29 calendar-sticky-headers overhaul (spec §10). Pattern mirrors
/// ErrorKeyParityTests.
/// </summary>
public class CalendarStickyKeyParityTests
{
    private static readonly string[] RequiredKeys = new[]
    {
        "Calendar_StickyToolbar_AriaLabel",
        "Calendar_StickyHeader_AriaLabel",
        "Calendar_PinnedGroup_AriaLabel",
        "Calendar_RowLabel_ColumnHeader",
        "Calendar_NextGroup",
        "Calendar_ScrollHint_Horizontal",
        "Calendar_ScrollHint_Vertical",
        "Calendar_RowMode_Shifts",
        "Calendar_RowMode_Users",
        "Calendar_RowMode_Duty",
        "Calendar_RowMode_Chores",
        "Calendar_Tools_Overflow_Label",
        "Calendar_Tools_Overflow_AriaLabel",
    };

    private static (HashSet<string> En, HashSet<string> He) LoadKeys()
    {
        var repoRoot = LocateRepoRoot();
        var enPath = Path.Combine(repoRoot, "Resources", "SharedResources.resx");
        var hePath = Path.Combine(repoRoot, "Resources", "SharedResources.he-IL.resx");

        return (ExtractKeys(enPath), ExtractKeys(hePath));
    }

    private static HashSet<string> ExtractKeys(string resxPath)
    {
        var doc = XDocument.Load(resxPath);
        return doc.Descendants("data")
            .Select(d => d.Attribute("name")?.Value ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);
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

    [Fact]
    public void AllRequiredKeysPresentInBothLanguages()
    {
        var (en, he) = LoadKeys();
        var missingEn = RequiredKeys.Where(k => !en.Contains(k)).ToList();
        var missingHe = RequiredKeys.Where(k => !he.Contains(k)).ToList();

        missingEn.Should().BeEmpty("English resx must contain every Calendar_* sticky key");
        missingHe.Should().BeEmpty("Hebrew resx must contain every Calendar_* sticky key");
    }

    [Fact]
    public void HebrewValuesAreNonEmpty()
    {
        var repoRoot = LocateRepoRoot();
        var hePath = Path.Combine(repoRoot, "Resources", "SharedResources.he-IL.resx");
        var doc = XDocument.Load(hePath);
        var heValues = doc.Descendants("data")
            .Where(d => RequiredKeys.Contains(d.Attribute("name")?.Value ?? ""))
            .ToDictionary(
                d => d.Attribute("name")!.Value,
                d => d.Element("value")?.Value ?? "");

        foreach (var key in RequiredKeys)
        {
            heValues.Should().ContainKey(key);
            heValues[key].Should().NotBeNullOrWhiteSpace($"{key} must have a Hebrew translation");
        }
    }
}
