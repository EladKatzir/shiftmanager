using System.Xml.Linq;
using FluentAssertions;

namespace ShiftManager.Tests.UnitTests.Localization;

/// <summary>
/// Enforces EN ↔ HE parity for all error- and feedback-related resx keys.
///
/// This test is the project's primary defense against silent localization regressions
/// during the error-handling overhaul: any new <c>Error_*</c> or <c>Feedback_*</c> key
/// added to one resx file MUST be added to the other in the same commit.
///
/// If you see this test fail, you've added a key to one language and not the other.
/// Fix: add the missing key with the appropriate translation.
/// </summary>
public class ErrorKeyParityTests
{
    private static readonly string[] PrefixesToCheck = { "Error_", "Feedback_" };

    private static (HashSet<string> En, HashSet<string> He) LoadKeys()
    {
        var repoRoot = LocateRepoRoot();
        var enPath = Path.Combine(repoRoot, "Resources", "SharedResources.resx");
        var hePath = Path.Combine(repoRoot, "Resources", "SharedResources.he-IL.resx");

        File.Exists(enPath).Should().BeTrue($"English resx must exist at {enPath}");
        File.Exists(hePath).Should().BeTrue($"Hebrew resx must exist at {hePath}");

        var en = ExtractKeys(enPath);
        var he = ExtractKeys(hePath);
        return (en, he);
    }

    private static HashSet<string> ExtractKeys(string resxPath)
    {
        var doc = XDocument.Load(resxPath);
        return doc.Descendants("data")
            .Select(d => d.Attribute("name")?.Value ?? string.Empty)
            .Where(k => PrefixesToCheck.Any(p => k.StartsWith(p, StringComparison.Ordinal)))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string LocateRepoRoot()
    {
        // Walk up from the test bin output until we find ShiftManager.csproj
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ShiftManager.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate ShiftManager.csproj walking up from " + AppContext.BaseDirectory);
    }

    [Fact]
    public void EveryErrorOrFeedbackKeyExistsInBothResxFiles()
    {
        var (en, he) = LoadKeys();

        var missingInHebrew = en.Except(he).OrderBy(k => k).ToList();
        var missingInEnglish = he.Except(en).OrderBy(k => k).ToList();

        var failures = new List<string>();
        if (missingInHebrew.Count > 0)
        {
            failures.Add($"Missing in Hebrew (he-IL.resx): {string.Join(", ", missingInHebrew)}");
        }
        if (missingInEnglish.Count > 0)
        {
            failures.Add($"Missing in English (.resx): {string.Join(", ", missingInEnglish)}");
        }

        failures.Should().BeEmpty(
            "every Error_* and Feedback_* key must exist in BOTH English and Hebrew resx files. " +
            "If you added a new key, add it to the other language too."
        );
    }

    [Fact]
    public void NoErrorOrFeedbackKeyHasEmptyValue()
    {
        var repoRoot = LocateRepoRoot();
        var paths = new[]
        {
            Path.Combine(repoRoot, "Resources", "SharedResources.resx"),
            Path.Combine(repoRoot, "Resources", "SharedResources.he-IL.resx")
        };

        var emptyKeys = new List<string>();
        foreach (var path in paths)
        {
            var doc = XDocument.Load(path);
            foreach (var data in doc.Descendants("data"))
            {
                var name = data.Attribute("name")?.Value ?? string.Empty;
                if (!PrefixesToCheck.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
                    continue;

                var value = data.Element("value")?.Value ?? string.Empty;
                if (string.IsNullOrWhiteSpace(value))
                {
                    emptyKeys.Add($"{Path.GetFileName(path)}: {name}");
                }
            }
        }

        emptyKeys.Should().BeEmpty("error/feedback keys must always have a non-empty translation");
    }
}
