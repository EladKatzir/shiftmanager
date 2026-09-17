namespace ShiftManager.ViewComponents;

/// <summary>One &lt;col&gt; in the calendar grid's colgroup.</summary>
/// <param name="Key">Persistence key: "label", "total", or "day:yyyy-MM-dd".</param>
/// <param name="CssClass">Class list for the rendered &lt;col&gt;.</param>
/// <param name="Width">Resolved width in CSS pixels (saved value, else the default).</param>
/// <param name="DefaultWidth">The width this column has with nothing saved. Emitted to the DOM so
/// double-click-to-reset does not have to duplicate the defaults in JavaScript.</param>
public sealed record CalendarColumn(string Key, string CssClass, int Width, int DefaultWidth);

/// <summary>
/// Builds the column plan behind the calendar's &lt;colgroup&gt;, which is what makes Excel-style
/// drag-resize work: JS sets width on a single &lt;col&gt; and every cell in that column follows,
/// instead of writing a width onto every &lt;td&gt;.
///
/// Extracted from the view (like <see cref="CalendarOrderApplier"/>) so the count invariant is
/// unit-testable: the number of columns MUST equal the colspan Default.cshtml uses for
/// group-header rows, or `table-layout: fixed` misaligns the whole grid.
/// </summary>
public static class CalendarColumnPlanner
{
    /// <summary>Matches `.excel-calendar__row-label { min-width: 150px }` in calendar.css.</summary>
    public const int DefaultLabelWidth = 150;

    /// <summary>Matches `.excel-calendar__header-day { min-width: 100px }` in calendar.css.</summary>
    public const int DefaultDayWidth = 100;

    /// <summary>Matches `.excel-calendar--compact .excel-calendar__header-day { min-width: 60px }`.</summary>
    public const int DefaultCompactDayWidth = 60;

    public const int DefaultTotalWidth = 80;

    /// <summary>Narrowest a column may be dragged. The single source of truth: the drag handle
    /// clamps to it, the save endpoint rejects below it, and the view emits it to the DOM.</summary>
    public const int MinWidth = 40;

    /// <summary>Widest a column may be dragged. See <see cref="MinWidth"/>.</summary>
    public const int MaxWidth = 600;

    public static List<CalendarColumn> Build(
        IReadOnlyList<DateOnly> days,
        bool hasShiftCount,
        bool isCompact,
        IReadOnlyDictionary<string, int> savedWidths)
    {
        ArgumentNullException.ThrowIfNull(days);
        ArgumentNullException.ThrowIfNull(savedWidths);

        // Saved rows are looked up BY the columns this render actually has, never iterated, so a
        // stale row (e.g. a day the user resized before navigating to another week) cannot add a
        // phantom column and break the colspan invariant.
        int Resolve(string key, int fallback) =>
            savedWidths.TryGetValue(key, out var w) ? w : fallback;

        var columns = new List<CalendarColumn>(days.Count + 2)
        {
            new("label", "excel-calendar__col excel-calendar__col--label",
                Resolve("label", DefaultLabelWidth), DefaultLabelWidth)
        };

        // Every day column shares the key "day", so a saved width applies to all of them and
        // survives navigation to another week or month. Keying per-date would silently drop the
        // user's sizing the moment the visible range changed, which reads as a bug rather than
        // as per-column precision.
        var dayDefault = isCompact ? DefaultCompactDayWidth : DefaultDayWidth;
        var dayWidth = Resolve("day", dayDefault);
        foreach (var _ in days)
        {
            columns.Add(new CalendarColumn("day", "excel-calendar__col excel-calendar__col--day",
                dayWidth, dayDefault));
        }

        if (hasShiftCount)
        {
            columns.Add(new CalendarColumn("total", "excel-calendar__col excel-calendar__col--total",
                Resolve("total", DefaultTotalWidth), DefaultTotalWidth));
        }

        return columns;
    }
}
