using System.Xml.Linq;
using FluentAssertions;

namespace ShiftManager.Tests.UnitTests;

/// <summary>
/// Regression guard for the 3 previously-unlocalized mail methods in <c>Services/MailService.cs</c>:
/// <c>SendTraineeAddedEmailAsync</c>, <c>SendSlotRemovedEmailAsync</c>, <c>SendShiftModifiedEmailAsync</c>.
///
/// Verifies each required <c>Email_*</c> key exists in both resx files, contains no emoji
/// (per <c>ResxEmojiLintTests</c> convention — existing localized emails are emoji-free),
/// has valid placeholder mirroring, and has Hebrew characters in the Hebrew resx.
/// </summary>
public class MailServiceLocalizationTests
{
    // Emoji detection — targets the specific characters that appeared in the three
    // migrated mail methods' HTML headers (👤 ⚠️ 🔄) plus the broader surrogate-pair
    // emoji range covering most pictographic symbols.
    private static readonly System.Text.RegularExpressions.Regex EmojiRx =
        new(@"[\uD83C-\uD83E][\uDC00-\uDFFF]|" + // surrogate-pair emoji (U+1F000+)
            @"[\u2600-\u27BF]|" +                  // misc symbols, dingbats, supplemental arrows
            @"[\u2190-\u21FF]|" +                  // arrows (some emoji-like)
            @"[\u2300-\u23FF]|" +                  // misc technical
            @"\uFE0F",                              // emoji variation selector
            System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly string[] RequiredKeys =
    {
        "Email_TraineeAdded_Subject",
        "Email_TraineeAdded_Title",
        "Email_TraineeAdded_Body",
        "Email_TraineeAdded_Highlight",
        "Email_TraineeAdded_Guidance",
        "Email_SlotRemoved_Subject",
        "Email_SlotRemoved_Title",
        "Email_SlotRemoved_Body",
        "Email_SlotRemoved_Reason",
        "Email_SlotRemoved_ContactManager",
        "Email_ShiftModified_Subject",
        "Email_ShiftModified_Title",
        "Email_ShiftModified_Body",
        "Email_ShiftModified_Changes",
        "Email_ShiftModified_ReviewPrompt",
    };

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ShiftManager.csproj")))
            {
                dir = dir.Parent;
            }
            return dir?.FullName ?? throw new InvalidOperationException("Could not locate repo root");
        }
    }

    private static Dictionary<string, string> LoadResx(string relativePath)
    {
        var doc = XDocument.Load(Path.Combine(RepoRoot, relativePath));
        return doc.Root!.Elements("data")
            .Where(d => d.Attribute("name") != null)
            .ToDictionary(
                d => d.Attribute("name")!.Value,
                d => d.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
    }

    [Fact]
    public void AllRequiredKeysPresentInBothResx()
    {
        var en = LoadResx(Path.Combine("Resources", "SharedResources.resx"));
        var he = LoadResx(Path.Combine("Resources", "SharedResources.he-IL.resx"));

        var missingEn = RequiredKeys.Where(k => !en.ContainsKey(k)).ToList();
        var missingHe = RequiredKeys.Where(k => !he.ContainsKey(k)).ToList();

        using var _ = new FluentAssertions.Execution.AssertionScope();
        missingEn.Should().BeEmpty("English resx missing keys: {0}", string.Join(", ", missingEn));
        missingHe.Should().BeEmpty("Hebrew resx missing keys: {0}", string.Join(", ", missingHe));
    }

    [Fact]
    public void NoEmojiInEmailBodyKeys()
    {
        // Existing 13 localized email methods use emoji-free resx; the 3 migrated methods
        // must follow the same convention (the colored CSS header bars are the visual cue).
        var en = LoadResx(Path.Combine("Resources", "SharedResources.resx"));
        var he = LoadResx(Path.Combine("Resources", "SharedResources.he-IL.resx"));

        var offenders = new List<string>();
        foreach (var key in RequiredKeys)
        {
            if (en.TryGetValue(key, out var enVal) && EmojiRx.IsMatch(enVal))
                offenders.Add($"EN/{key}: {enVal}");
            if (he.TryGetValue(key, out var heVal) && EmojiRx.IsMatch(heVal))
                offenders.Add($"HE/{key}: {heVal}");
        }

        offenders.Should().BeEmpty(
            "Email body resx must be emoji-free per ResxEmojiLintTests convention. Offenders: {0}",
            string.Join(" | ", offenders));
    }

    [Fact]
    public void HebrewValuesContainHebrewCharacters()
    {
        // Prevents accidentally copying English into the Hebrew slot during migration.
        var he = LoadResx(Path.Combine("Resources", "SharedResources.he-IL.resx"));
        var hebrewRx = new System.Text.RegularExpressions.Regex(@"\p{IsHebrew}",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        var englishOnly = new List<string>();
        foreach (var key in RequiredKeys)
        {
            if (!he.TryGetValue(key, out var val)) continue;
            if (!hebrewRx.IsMatch(val))
                englishOnly.Add($"{key}: {val}");
        }

        englishOnly.Should().BeEmpty(
            "Hebrew resx values must contain at least one Hebrew character. Offenders: {0}",
            string.Join(" | ", englishOnly));
    }

    [Fact]
    public void SubjectKeysAcceptTwoParams_shiftTypeAndDate()
    {
        // Each subject method call is string.Format(_localizer[key], shiftType, date)
        // so both {0} and {1} must appear.
        var en = LoadResx(Path.Combine("Resources", "SharedResources.resx"));
        string[] subjectKeys = { "Email_TraineeAdded_Subject", "Email_SlotRemoved_Subject", "Email_ShiftModified_Subject" };

        using var _ = new FluentAssertions.Execution.AssertionScope();
        foreach (var key in subjectKeys)
        {
            en.TryGetValue(key, out var val).Should().BeTrue($"{key} must exist");
            val.Should().Contain("{0}", $"{key} must have a {{0}} placeholder for shiftType");
            val.Should().Contain("{1}", $"{key} must have a {{1}} placeholder for date");
        }
    }
}
