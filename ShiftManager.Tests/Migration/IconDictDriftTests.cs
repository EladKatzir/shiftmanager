using System.Text.RegularExpressions;
using FluentAssertions;

namespace ShiftManager.Tests.Migration;

/// <summary>
/// Guards against drift between the server icon dictionary in
/// <c>TagHelpers/IconTagHelper.cs</c> and the client-side dictionary in
/// <c>wwwroot/js/icon-runtime.js</c>. The server is the source of truth;
/// every icon the client renders must also exist on the server, so a
/// dynamically-built nav item never gets an SVG the server can't also emit.
///
/// The converse is allowed — the server may legitimately have icons the
/// client doesn't need yet (e.g. server-only pages).
/// </summary>
public class IconDictDriftTests
{
    [Fact]
    public void ClientIconNames_MustBeSubsetOfServerIconNames()
    {
        var repoRoot = FindRepoRoot();
        repoRoot.Should().NotBeNull("tests must run from somewhere under the ShiftManager repo");

        var serverPath = Path.Combine(repoRoot!, "TagHelpers", "IconTagHelper.cs");
        var clientPath = Path.Combine(repoRoot,  "wwwroot",    "js", "icon-runtime.js");

        File.Exists(serverPath).Should().BeTrue($"expected {serverPath}");
        File.Exists(clientPath).Should().BeTrue($"expected {clientPath}");

        var serverNames = ExtractServerIconKeys(File.ReadAllText(serverPath));
        var clientNames = ExtractClientIconKeys(File.ReadAllText(clientPath));

        serverNames.Should().NotBeEmpty();
        clientNames.Should().NotBeEmpty();

        var missing = clientNames.Except(serverNames).ToList();
        missing.Should().BeEmpty(
            "every icon in icon-runtime.js must exist in IconTagHelper.cs — missing: " +
            string.Join(", ", missing));
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ShiftManager.csproj"))) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    // Server format: `["name"] = "<path .../>",`
    private static HashSet<string> ExtractServerIconKeys(string cs)
    {
        var pattern = new Regex("""\["([a-z][a-z0-9\-]*)"\]\s*=""");
        return pattern.Matches(cs)
            .Select(m => m.Groups[1].Value)
            .ToHashSet();
    }

    // Client format: `'name': '<path .../>',`
    private static HashSet<string> ExtractClientIconKeys(string js)
    {
        var pattern = new Regex(@"'([a-z][a-z0-9\-]*)'\s*:\s*'<");
        return pattern.Matches(js)
            .Select(m => m.Groups[1].Value)
            .ToHashSet();
    }
}
