using FluentAssertions;

namespace ShiftManager.Tests.Helpers;

/// <summary>
/// Shared helpers for CSS-text guard tests (the family of tests that parse
/// wwwroot/css/*.css or Resources/*.resx and assert invariants on the text).
///
/// Centralizing these here eliminates the convention drift that accumulated
/// across CalendarStickyTokenTests, CalendarStickyRtlSweepTests, and
/// CalendarStickyKeyParityTests (each previously had its own copy of
/// LocateRepoRoot, with different throw messages and guard-clause styles).
/// </summary>
public static class CssTestHelpers
{
    /// <summary>
    /// Walks up from AppContext.BaseDirectory until the directory containing
    /// ShiftManager.csproj is found. Used by every CSS/resx-text test to
    /// locate the repo root deterministically across `dotnet test` and IDE
    /// runners alike.
    /// </summary>
    public static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ShiftManager.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate ShiftManager.csproj walking up from " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// Reads the named file under the repo root and asserts it exists with
    /// a FluentAssertions-friendly failure message. Centralizes the
    /// File.Exists guard pattern from ErrorKeyParityTests.
    /// </summary>
    public static string ReadRepoFile(params string[] segments)
    {
        var path = Path.Combine(new[] { LocateRepoRoot() }.Concat(segments).ToArray());
        File.Exists(path).Should().BeTrue($"required file must exist at {path}");
        return File.ReadAllText(path);
    }
}
