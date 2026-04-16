using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;

namespace ShiftManager.Tests.Migration;

/// <summary>
/// Phase-3 gate: after the reductive emoji strip, no emoji characters should
/// remain in SharedResources.resx or SharedResources.he-IL.resx. UI chrome
/// icons are now rendered via the &lt;icon&gt; tag helper next to the localized
/// string, not baked into the translation itself.
///
/// ACTIVE — CI will fail if any PR reintroduces emoji to the .resx files.
/// Plain-arrow codepoints (U+2190-U+21FF) are intentionally allowed because
/// "→" is legitimate typography (e.g. "Service → Federation Service Properties").
/// </summary>
public class ResxEmojiLintTests
{
    // Emoji Unicode ranges. Deliberately EXCLUDES the plain-arrow block (U+2190-U+21FF)
    // because "→" and friends are legitimate typography in instructions (e.g. "File → Open").
    // Also excludes U+FE0F on its own since the variation selector is only meaningful paired
    // with a base glyph; any preceding base glyph will already have been caught.
    private static readonly Regex EmojiPattern = new(
        @"[\uD83C-\uDBFF][\uDC00-\uDFFF]" +          // surrogate-pair emoji (U+10000+)
        @"|[\u2600-\u27BF]" +                          // misc symbols + dingbats (☰ ✅ ⚠ ⭐ …)
        @"|[\u2300-\u23FF]" +                          // misc technical (⌨ ⌛ ⏰ …)
        @"|[\u2B00-\u2BFF]",                           // arrows / stars (emoji range)
        RegexOptions.Compiled);

    [Theory]
    [InlineData("SharedResources.resx")]
    [InlineData("SharedResources.he-IL.resx")]
    public void Resx_ShouldNotContainEmojis(string fileName)
    {
        var path = FindResxFile(fileName);
        path.Should().NotBeNull($"{fileName} must exist in Resources/");

        var doc = XDocument.Load(path!);
        var offenders = doc.Descendants("data")
            .Select(d => new
            {
                Key   = d.Attribute("name")?.Value ?? "(unknown)",
                Value = d.Element("value")?.Value ?? string.Empty
            })
            .Where(entry => EmojiPattern.IsMatch(entry.Value))
            .ToList();

        offenders.Should().BeEmpty(
            "emoji chars must be rendered via the <icon> tag helper, not baked into translations. " +
            "Offenders: " + string.Join("; ", offenders.Select(o => $"{o.Key}: {o.Value}")));
    }

    private static string? FindResxFile(string fileName)
    {
        // Walk up from bin/Debug/... to repo root, then into Resources/.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Resources", fileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
