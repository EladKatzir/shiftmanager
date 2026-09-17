using ShiftManager.ViewComponents;

namespace ShiftManager.Tests.ViewComponents;

/// <summary>
/// The column plan drives the &lt;colgroup&gt; that makes Excel-style drag-resize possible.
///
/// The load-bearing invariant is the COUNT: Default.cshtml renders group-header rows with
/// colspan="days + 1 + (hasShiftCount ? 1 : 0)". A &lt;colgroup&gt; with a different number of
/// &lt;col&gt; elements silently misaligns every column under `table-layout: fixed`, so the count
/// is pinned here rather than left to the view.
/// </summary>
public class CalendarColumnPlannerTests
{
    private static List<DateOnly> Days(int n) =>
        Enumerable.Range(0, n).Select(i => new DateOnly(2026, 9, 14).AddDays(i)).ToList();

    private static readonly Dictionary<string, int> NoSaved = new();

    /// <summary>Day columns, asserting there is at least one — otherwise an Assert.All over the
    /// result would pass vacuously and the test would prove nothing.</summary>
    private static List<CalendarColumn> DayCols(IEnumerable<CalendarColumn> plan, int expected)
    {
        var cols = plan.Where(c => c.Key == "day").ToList();
        Assert.Equal(expected, cols.Count);
        return cols;
    }

    [Fact]
    public void ColumnCount_MatchesGroupHeaderColspan_WithoutTotal()
    {
        var plan = CalendarColumnPlanner.Build(Days(7), hasShiftCount: false, isCompact: false, NoSaved);
        Assert.Equal(7 + 1, plan.Count);
    }

    [Fact]
    public void ColumnCount_MatchesGroupHeaderColspan_WithTotal()
    {
        var plan = CalendarColumnPlanner.Build(Days(7), hasShiftCount: true, isCompact: false, NoSaved);
        Assert.Equal(7 + 1 + 1, plan.Count);
    }

    [Fact]
    public void ColumnKeys_AreLabelThenDaysThenTotal()
    {
        // Every day column shares the SINGLE key "day". Keying them per-date would make a width
        // evaporate the moment the user navigates to another week, which reads as a bug; sharing
        // one key means "make the day columns wider" survives navigation and week/month switching.
        var plan = CalendarColumnPlanner.Build(Days(2), hasShiftCount: true, isCompact: false, NoSaved);
        Assert.Equal(
            new[] { "label", "day", "day", "total" },
            plan.Select(c => c.Key).ToArray());
    }

    [Fact]
    public void WithNothingSaved_DayColumnsUseTheDefaultWidth()
    {
        var plan = CalendarColumnPlanner.Build(Days(3), hasShiftCount: false, isCompact: false, NoSaved);
        Assert.All(DayCols(plan, 3), c => Assert.Equal(CalendarColumnPlanner.DefaultDayWidth, c.Width));
        Assert.Equal(CalendarColumnPlanner.DefaultLabelWidth, plan[0].Width);
    }

    [Fact]
    public void CompactMonthView_UsesTheNarrowerDayDefault()
    {
        var plan = CalendarColumnPlanner.Build(Days(3), hasShiftCount: false, isCompact: true, NoSaved);
        Assert.All(DayCols(plan, 3), c => Assert.Equal(CalendarColumnPlanner.DefaultCompactDayWidth, c.Width));
    }

    [Fact]
    public void SavedDayWidth_AppliesToEveryDayColumn_ButNotToLabelOrTotal()
    {
        var saved = new Dictionary<string, int> { ["day"] = 240 };
        var plan = CalendarColumnPlanner.Build(Days(3), hasShiftCount: true, isCompact: false, saved);

        Assert.All(DayCols(plan, 3), c => Assert.Equal(240, c.Width));
        Assert.Equal(CalendarColumnPlanner.DefaultLabelWidth, plan[0].Width);
        Assert.Equal(CalendarColumnPlanner.DefaultTotalWidth, plan[^1].Width);
    }

    [Fact]
    public void SavedDayWidth_SurvivesNavigationToADifferentDateRange()
    {
        // The whole point of the shared "day" key: the same saved row applies to any date range.
        var saved = new Dictionary<string, int> { ["day"] = 240 };
        var thisWeek = CalendarColumnPlanner.Build(Days(7), false, false, saved);
        var nextWeek = CalendarColumnPlanner.Build(
            Enumerable.Range(0, 7).Select(i => new DateOnly(2026, 12, 1).AddDays(i)).ToList(),
            false, false, saved);

        Assert.All(DayCols(thisWeek, 7), c => Assert.Equal(240, c.Width));
        Assert.All(DayCols(nextWeek, 7), c => Assert.Equal(240, c.Width));
    }

    [Fact]
    public void UnknownSavedKey_IsIgnored_AndAddsNoPhantomColumn()
    {
        var saved = new Dictionary<string, int> { ["day:2025-01-01"] = 240, ["label"] = 200 };
        var plan = CalendarColumnPlanner.Build(Days(3), hasShiftCount: false, isCompact: false, saved);

        Assert.Equal(3 + 1, plan.Count);
        Assert.Equal(200, plan[0].Width);
        Assert.All(DayCols(plan, 3), c => Assert.Equal(CalendarColumnPlanner.DefaultDayWidth, c.Width));
    }

    [Fact]
    public void EachColumn_CarriesItsDefaultWidth_SoTheClientCanResetWithoutHardcodingIt()
    {
        var saved = new Dictionary<string, int> { ["day"] = 240 };
        var plan = CalendarColumnPlanner.Build(Days(3), hasShiftCount: false, isCompact: false, saved);

        var resized = plan.First(c => c.Key == "day");
        Assert.Equal(240, resized.Width);
        Assert.Equal(CalendarColumnPlanner.DefaultDayWidth, resized.DefaultWidth);
        Assert.Equal(CalendarColumnPlanner.DefaultLabelWidth, plan[0].DefaultWidth);
    }

    [Fact]
    public void SavedTotalWidth_IsIgnored_WhenThereIsNoTotalColumn()
    {
        var saved = new Dictionary<string, int> { ["total"] = 300 };
        var plan = CalendarColumnPlanner.Build(Days(2), hasShiftCount: false, isCompact: false, saved);

        Assert.Equal(2 + 1, plan.Count);
        Assert.DoesNotContain(plan, c => c.Key == "total");
    }
}
