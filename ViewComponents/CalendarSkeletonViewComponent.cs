using Microsoft.AspNetCore.Mvc;

namespace ShiftManager.ViewComponents;

/// <summary>
/// Calendar skeleton loading component for calendar views.
/// Implements B-007: Skeleton Loading for Calendars.
/// </summary>
public class CalendarSkeletonViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(string viewType = "month", int itemCount = 5, int cellCount = 35)
    {
        return View(new CalendarSkeletonModel
        {
            ViewType = viewType,
            ItemCount = itemCount,
            CellCount = cellCount
        });
    }
}

/// <summary>
/// Model for calendar skeleton loading component.
/// </summary>
public class CalendarSkeletonModel
{
    /// <summary>
    /// Type of calendar view: "month", "week", "day", "table"
    /// </summary>
    public string ViewType { get; set; } = "month";

    /// <summary>
    /// Number of item placeholders to show per cell/column.
    /// Default: 5 for day view, 2-3 for others.
    /// </summary>
    public int ItemCount { get; set; } = 5;

    /// <summary>
    /// Number of cells to render (for month view).
    /// Default: 35 (5 weeks).
    /// </summary>
    public int CellCount { get; set; } = 35;

    /// <summary>
    /// Helper: Get number of columns for week view (always 7).
    /// </summary>
    public int WeekColumns => 7;

    /// <summary>
    /// Helper: Get number of table rows for table view skeleton.
    /// </summary>
    public int TableRows => 4;

    /// <summary>
    /// Helper: Get number of date columns for table view skeleton.
    /// </summary>
    public int TableDateColumns => 7;
}
