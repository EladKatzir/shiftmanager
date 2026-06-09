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
    public string? AssignmentTooltip { get; set; }  // e.g. "Assigned by X on Y" — set by oncall calendar

    // HOME unification (Task 22) — let the renderer compose chips with source-icon prefix.
    // IsHome is true for ShiftType.IsHome assignments; the chip then renders source/house icons + time range.
    public bool IsHome { get; set; }
    public string? ShiftStart { get; set; }  // pre-formatted "HH:mm" for chip time label
    public string? ShiftEnd { get; set; }    // pre-formatted "HH:mm" for chip time label

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

public class ExcelCalendarTableViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(ExcelCalendarTableViewModel model)
    {
        return View(model);
    }
}
