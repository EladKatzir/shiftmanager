using Microsoft.AspNetCore.Mvc;

namespace ShiftManager.ViewComponents;

public class ExcelCalendarTableViewModel
{
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string ViewMode { get; set; } = "week"; // week, 2weeks, month
    public bool IsReadOnly { get; set; }
    public string CalendarType { get; set; } = "shifts"; // shifts, chores, oncall, overview
    public List<ExcelCalendarRow> Rows { get; set; } = new();
    public List<ExcelCalendarGroup>? Groups { get; set; }

    /// <summary>Active Draft Mode session id (Epic 4). When set, the grid renders the sandbox overlay and
    /// the remove (×) on shift chips stages a draft-clear instead of a live delete.</summary>
    public int? DraftSessionId { get; set; }

    /// <summary>
    /// Localization NameKeys of the grant(s) that would unlock editing this calendar. Populated only
    /// when <see cref="IsReadOnly"/> is true AND an obtainable grant exists (empty for genuinely
    /// view-only calendars such as Overview). Drives the read-only banner's "?" help button, which
    /// tells the user exactly which permission to request.
    /// </summary>
    public List<string> RequiredGrantNameKeys { get; set; } = new();

    /// <summary>
    /// What each row represents. PascalCase ("Shifts" | "Users" | "Duty" | "Chores")
    /// so it appends directly to the resx key "Calendar_RowMode_" + RowMode.
    /// Used by the sticky corner-cell mode token (spec §5.6).
    /// </summary>
    public string RowMode { get; set; } = "Shifts";

    /// <summary>
    /// Total row count INCLUDING the &lt;thead&gt; column-header row AND group-header rows.
    /// Per ARIA 1.2 §6.6.4, aria-rowcount represents the total &lt;tr&gt; count in the
    /// logical table; the column header is row 1, the first data row is row 2 (the
    /// aria-rowindex tracking in Task 13 starts at 2 inside &lt;tbody&gt; for that reason).
    /// Caller PageModels must set this as Rows.Count + (Groups?.Count ?? 0) + 1.
    /// </summary>
    public int TotalRows { get; set; }

    /// <summary>
    /// resx key for the mode label, computed from RowMode.
    /// Defaults to "Calendar_RowMode_Shifts" for unset instances (RowMode initializer = "Shifts").
    /// </summary>
    public string RowModeLabelKey => $"Calendar_RowMode_{RowMode}";

    /// <summary>Per-user reorder context key, e.g. "shifts:9:2:user". Null disables reordering for this render.</summary>
    public string? RowOrderContextKey { get; set; }

    /// <summary>
    /// Day-scoped free-text notes (<see cref="ShiftManager.Models.CalendarDayNote"/>) keyed by date,
    /// rendered in the date-column headers. Company-wide and view-independent, so they show in both
    /// shift-mode and user-mode. Populated only by calendars that support day notes (Shifts); empty elsewhere.
    /// </summary>
    public Dictionary<DateOnly, string> DayNotes { get; set; } = new();
}

public class ExcelCalendarRow
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Icon { get; set; }  // Lucide icon name (e.g. "shield", "star"). Rendered via <icon> tag helper when set.
    public string? SubLabel { get; set; }  // Secondary info line (rotation group, emergency tier)
    public string? Color { get; set; }
    public string? GroupId { get; set; }
    public string? CompanyName { get; set; }
    public double? WeeklyHours { get; set; }
    /// <summary>Count of real (non-home/non-offline) shifts assigned to this row's user within the
    /// visible date range (#8). Drives the by-user week-view "Total" column; WeeklyHours is retained
    /// but no longer rendered there.</summary>
    public int? ShiftCount { get; set; }
    public Dictionary<DateOnly, ExcelCalendarCell> Cells { get; set; } = new();
}

public class ExcelCalendarGroup
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsCollapsed { get; set; }
    public int SortOrder { get; set; }
    public string? Color { get; set; }
    public int MemberCount { get; set; }
}

public class ExcelCalendarCell
{
    public List<ExcelCalendarAssignment> Assignments { get; set; } = new();
    public ExcelCalendarOverlay? Overlay { get; set; }
    public string? Note { get; set; }
    public int? Capacity { get; set; }
    public int? DefaultCapacity { get; set; }
}

public class ExcelCalendarAssignment
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? SubLabel { get; set; }  // Note or annotation for this assignment
    public string? Role { get; set; }
    public bool IsTrainee { get; set; }
    public bool IsTraineeShift { get; set; }  // Shikma: trainee takes shift directly
    public int? UserId { get; set; }
    public int? TraineeUserId { get; set; }
    public string? TraineeName { get; set; }
    /// <summary>Owning shift type — set ONLY for real shift assignments (not text/chore/overlay chips).
    /// Drives Draft Mode removal (× → stage-clear by shiftType+date+user) and is null elsewhere.</summary>
    public int? ShiftTypeId { get; set; }

    /// <summary>Chores Draft Mode (Spec C): the staged chore descriptor's stable key. Set ONLY on chore chips
    /// while a chores draft is active; the × then stages a draft-clear by (user, date, descriptorKey) since a
    /// staged chore has no <c>Chore.Id</c>. Null on live/non-draft chips.</summary>
    public string? DraftChoreKey { get; set; }
    public string? AssignmentTooltip { get; set; }  // e.g. "Assigned by X on Y" — set by oncall calendar

    // HOME unification (Task 22) — let the renderer compose chips with source-icon prefix.
    // IsHome is true for ShiftType.IsHome assignments; the chip then renders source/house icons + time range.
    public bool IsHome { get; set; }
    public string? ShiftStart { get; set; }  // pre-formatted "HH:mm" for chip time label
    public string? ShiftEnd { get; set; }    // pre-formatted "HH:mm" for chip time label
    // Tab (לשונית) ghosting: true when this shift belongs to a DIFFERENT tab than the one being viewed.
    // Rendered greyed + non-interactive ("busy elsewhere") in by-user mode so the tab reads as a separate
    // calendar while the person's cross-tab commitments stay visible (and conflict/hours still count them).
    public bool IsBusyElsewhere { get; set; }

    // Source-of-truth for HOME chip's source icon (Task 22):
    //   null → rotation HOME (icon = repeat)
    //   set + Type=Vacation (0) → vacation HOME (icon = plane)
    //   set + Type=After    (1) → after HOME    (icon = sunrise)
    public int? SourceTimeOffRequestId { get; set; }
    public int? SourceTimeOffRequestType { get; set; }  // (int?)TimeOffType — 0=Vacation, 1=After
}

public class ExcelCalendarOverlay
{
    public bool HasVacation { get; set; }
    // "Day at [X]" time-off label (TimeOffType.DayAt). Non-null → render as a "יום {Label}" badge
    // instead of the vacation palm-tree. (Issue: day-X rendered as vacation symbol)
    public string? DayAtLabel { get; set; }
    public bool HasChore { get; set; }
    public bool HasOnDuty { get; set; }
    public bool HasTextEntry { get; set; }
    public bool HasOverviewNote { get; set; }
    public string? OverviewNoteText { get; set; }
    public List<string> OtherItems { get; set; } = new();
    public List<string> TextEntryTexts { get; set; } = new();
}

public static class CalendarOrderApplier
{
    /// <summary>In-place stable reorder of groups (by "" namespace) and rows (by their group namespace).
    /// Positioned items first in saved SortOrder; un-positioned keep default order after them.</summary>
    public static void Apply(
        List<ExcelCalendarRow> rows,
        List<ExcelCalendarGroup>? groups,
        Dictionary<(string GroupId, string RowId), int> order)
    {
        if (order.Count == 0) return;

        if (groups != null && groups.Count > 0)
        {
            var idx = 0;
            var ordered = groups
                .Select(g => new { g, i = idx++ })
                .OrderBy(x => order.TryGetValue(("", x.g.Id), out var so) ? 0 : 1)
                .ThenBy(x => order.TryGetValue(("", x.g.Id), out var so) ? so : x.g.SortOrder)
                .Select(x => x.g)
                .ToList();
            // Default.cshtml renders groups via `Model.Groups.OrderBy(g => g.SortOrder)`, so reordering
            // the list alone is ignored — we must REWRITE SortOrder to the computed position. (Reassigning
            // for every group keeps the relative order stable for un-positioned groups too.)
            for (var k = 0; k < ordered.Count; k++) ordered[k].SortOrder = k;
            groups.Clear();
            groups.AddRange(ordered);
        }

        var ri = 0;
        var orderedRows = rows
            .Select(r => new { r, i = ri++ })
            .OrderBy(x => order.TryGetValue((x.r.GroupId ?? "", x.r.Id), out _) ? 0 : 1)
            .ThenBy(x => order.TryGetValue((x.r.GroupId ?? "", x.r.Id), out var so) ? so : x.i)
            .Select(x => x.r)
            .ToList();
        // NOTE: Default.cshtml filters rows by group, so cross-group relative order is irrelevant;
        // within each group the stable sort above yields positioned-then-default. Group BLOCK order
        // is driven by `groups` above.
        rows.Clear();
        rows.AddRange(orderedRows);
    }
}

public class ExcelCalendarTableViewComponent : ViewComponent
{
    private readonly ShiftManager.Services.ICalendarRowOrderService _rowOrder;
    public ExcelCalendarTableViewComponent(ShiftManager.Services.ICalendarRowOrderService rowOrder)
        => _rowOrder = rowOrder;

    public async Task<IViewComponentResult> InvokeAsync(ExcelCalendarTableViewModel model)
    {
        if (!string.IsNullOrEmpty(model.RowOrderContextKey))
        {
            var idClaim = UserClaimsPrincipal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(idClaim, out var userId))
            {
                var map = await _rowOrder.GetOrderMapAsync(userId, model.RowOrderContextKey);
                CalendarOrderApplier.Apply(model.Rows, model.Groups, map);
            }
        }
        return View(model);
    }
}
