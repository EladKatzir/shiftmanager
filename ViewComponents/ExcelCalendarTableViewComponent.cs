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
