using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;

namespace ShiftManager.Tests.IntegrationTests;

/// <summary>
/// Permanent CI gate against localization regressions. Runs on every build.
///
/// - <c>EveryKeyHasPairInBothFiles</c>: neither resx file may have a key the other lacks.
/// - <c>PlaceholdersMatchAcrossCultures</c>: if English has <c>{0}</c>, Hebrew must too —
///   prevents format-string runtime crashes.
/// - <c>NoHardcodedEnglishInBadRequest</c>: API endpoints must not return raw English strings.
/// - <c>NoHardcodedEnglishInJsAlerts</c>: JS alert/confirm calls must not pass raw English.
///
/// This suite was added as part of the Hebrew-default rollout — see the plan at
/// <c>~/.claude/plans/we-need-to-add-peaceful-wirth.md</c> Phase 4.1.
/// </summary>
public class LocalizationDriftTests
{
    private static string RepoRoot
    {
        get
        {
            // Tests run from ShiftManager.Tests/bin/Debug/net8.0/ — walk up to the solution root.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ShiftManager.csproj")))
            {
                dir = dir.Parent;
            }
            if (dir == null)
            {
                throw new InvalidOperationException(
                    "Could not locate ShiftManager.csproj by walking up from " + AppContext.BaseDirectory);
            }
            return dir.FullName;
        }
    }

    private static Dictionary<string, string> LoadResx(string relativePath)
    {
        var path = Path.Combine(RepoRoot, relativePath);
        var doc = XDocument.Load(path);
        return doc.Root!.Elements("data")
            .Where(d => d.Attribute("name") != null)
            .ToDictionary(
                d => d.Attribute("name")!.Value,
                d => d.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
    }

    [Fact]
    public void EveryKeyHasPairInBothFiles()
    {
        var en = LoadResx(Path.Combine("Resources", "SharedResources.resx"));
        var he = LoadResx(Path.Combine("Resources", "SharedResources.he-IL.resx"));

        var inEnNotHe = en.Keys.Except(he.Keys).OrderBy(k => k).ToList();
        var inHeNotEn = he.Keys.Except(en.Keys).OrderBy(k => k).ToList();

        using var _ = new FluentAssertions.Execution.AssertionScope();
        inEnNotHe.Should().BeEmpty(
            "every English key must have a Hebrew pair — missing: {0}",
            string.Join(", ", inEnNotHe));
        inHeNotEn.Should().BeEmpty(
            "every Hebrew key must have an English pair — missing: {0}",
            string.Join(", ", inHeNotEn));
    }

    [Fact]
    public void PlaceholdersMatchAcrossCultures()
    {
        var en = LoadResx(Path.Combine("Resources", "SharedResources.resx"));
        var he = LoadResx(Path.Combine("Resources", "SharedResources.he-IL.resx"));
        var placeholderRx = new Regex(@"\{(\d+)\}", RegexOptions.Compiled);

        var mismatches = new List<string>();
        foreach (var (key, enValue) in en)
        {
            if (!he.TryGetValue(key, out var heValue)) continue; // caught by EveryKeyHasPairInBothFiles

            var enPlaceholders = placeholderRx.Matches(enValue)
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .OrderBy(x => x)
                .ToArray();
            var hePlaceholders = placeholderRx.Matches(heValue)
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .OrderBy(x => x)
                .ToArray();

            if (!enPlaceholders.SequenceEqual(hePlaceholders))
            {
                mismatches.Add(
                    $"{key}: en=[{string.Join(",", enPlaceholders)}] he=[{string.Join(",", hePlaceholders)}]");
            }
        }

        mismatches.Should().BeEmpty(
            "if English has {{0}}, Hebrew must too — otherwise string.Format throws at runtime. Mismatches: {0}",
            string.Join(" | ", mismatches));
    }

    // ---- Hardcoded-English scans -----------------------------------------------------

    /// <summary>
    /// Allowlist for legitimate hardcoded English in <c>BadRequest(...)</c> calls.
    /// Key names passed to the client for AppLocalizer lookup are NOT English strings —
    /// ScheduleExport_DateRangeExceeded is a key, not a sentence. Expand this list as
    /// needed with a clear justification comment.
    /// </summary>
    private static readonly HashSet<string> BadRequestAllowlist = new(StringComparer.Ordinal)
    {
        "ScheduleExport_DateRangeExceeded", // key-form message consumed by AppLocalizer lookup
    };

    [Fact]
    public void NoHardcodedEnglishInBadRequestReturns()
    {
        // Catches things like: return BadRequest("Invalid export request")
        // Allows: return BadRequest(_localizer["..."].Value) / BadRequest(new { key = "..." })
        var offenders = new List<string>();
        var rx = new Regex(@"BadRequest\(\s*""([A-Z][A-Za-z ]+)""\s*\)", RegexOptions.Compiled);

        foreach (var file in ScanSourceFiles("*.cs"))
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var m = rx.Match(lines[i]);
                if (!m.Success) continue;
                var literal = m.Groups[1].Value;
                if (BadRequestAllowlist.Contains(literal)) continue;
                offenders.Add($"{RelativePath(file)}:{i + 1}  BadRequest(\"{literal}\")");
            }
        }

        offenders.Should().BeEmpty(
            "API endpoints must localize error strings via _localizer[...]. Offenders: {0}",
            string.Join(" | ", offenders));
    }

    /// <summary>
    /// Allowlist for legitimate hardcoded English in JS <c>alert()</c>/<c>confirm()</c> calls —
    /// e.g. developer-facing diagnostics that users never see. Expand only with justification.
    /// </summary>
    private static readonly HashSet<string> JsAlertAllowlist = new(StringComparer.Ordinal);

    [Fact]
    public void NoHardcodedEnglishInJsAlerts()
    {
        // Catches: alert('This is English'), confirm('Are you sure?')
        // Allows: alert(L('KeyName')), confirm(myVar), alert(`template ${...}`)
        var offenders = new List<string>();
        var rx = new Regex(@"\b(alert|confirm)\s*\(\s*'([A-Z][A-Za-z ][^']*)'", RegexOptions.Compiled);

        foreach (var file in ScanSourceFiles("*.js"))
        {
            // Skip minified/vendor files if any creep in — heuristic by filename pattern.
            if (Path.GetFileName(file).Contains(".min.")) continue;

            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var m = rx.Match(lines[i]);
                if (!m.Success) continue;
                var literal = m.Groups[2].Value;
                if (JsAlertAllowlist.Contains(literal)) continue;
                offenders.Add($"{RelativePath(file)}:{i + 1}  {m.Groups[1].Value}('{literal}...')");
            }
        }

        offenders.Should().BeEmpty(
            "JS alert/confirm calls must use localized strings via window.AppLocalizer. Offenders: {0}",
            string.Join(" | ", offenders));
    }

    // ---- Helpers ---------------------------------------------------------------------

    private static IEnumerable<string> ScanSourceFiles(string pattern)
    {
        var roots = new[] { "Pages", "Services", "Middleware", "wwwroot/js" }
            .Select(p => Path.Combine(RepoRoot, p))
            .Where(Directory.Exists);

        // Skip generated/backup areas.
        var excludes = new[] { "obj", "bin", "Backups", "FinalProductPublish", "ProjectPublish" };

        foreach (var root in roots)
        {
            foreach (var file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(RepoRoot, file);
                if (excludes.Any(ex => rel.Contains(Path.DirectorySeparatorChar + ex + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase)
                    || rel.StartsWith(ex + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                yield return file;
            }
        }
    }

    private static string RelativePath(string fullPath) => Path.GetRelativePath(RepoRoot, fullPath);
}
