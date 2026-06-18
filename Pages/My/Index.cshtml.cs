using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Models.Schedule;
using ShiftManager.Services;

namespace ShiftManager.Pages.My;

[Authorize]
public class IndexModel : PageModel
{
    private readonly IPersonalTimelineService _timeline;
    public IndexModel(IPersonalTimelineService timeline) => _timeline = timeline;

    public enum ViewMode { Upcoming, All, Past30Days }
    public enum TimeRange { Week, Month, Custom }

    public List<TimelineItem> Items { get; set; } = new();
    public StatsData Stats { get; set; } = new(0, 0, 0, 0, 0, 0, 0, 0);
    public ViewMode CurrentView { get; set; } = ViewMode.Upcoming;
    public TimeRange CurrentTimeRange { get; set; } = TimeRange.Month;
    public DateOnly RangeStart { get; set; }
    public DateOnly RangeEnd { get; set; }

    public async Task<IActionResult> OnGetAsync(
        string? view = "upcoming",
        string? range = "month",
        DateOnly? customStart = null,
        DateOnly? customEnd = null)
    {
        // SECURITY FIX: Use TryParse to prevent crashes
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            return Page();
        }

        // Parse view mode
        CurrentView = view?.ToLower() switch
        {
            "all" => ViewMode.All,
            "past30days" => ViewMode.Past30Days,
            _ => ViewMode.Upcoming
        };

        // Calculate date range
        var today = DateOnly.FromDateTime(DateTime.Today);
        CurrentTimeRange = range?.ToLower() switch
        {
            "week" => TimeRange.Week,
            "custom" => TimeRange.Custom,
            _ => TimeRange.Month
        };

        (RangeStart, RangeEnd) = CurrentTimeRange switch
        {
            TimeRange.Week => (today, today.AddDays(7)),
            TimeRange.Custom when customStart.HasValue && customEnd.HasValue => (customStart.Value, customEnd.Value),
            _ => (today, today.AddMonths(1)) // Month (default)
        };

        // Adjust range based on view mode
        if (CurrentView == ViewMode.Past30Days)
        {
            RangeStart = today.AddDays(-30);
            RangeEnd = today;
        }
        else if (CurrentView == ViewMode.All)
        {
            RangeStart = today.AddMonths(-1);
            RangeEnd = today.AddMonths(3);
        }

        // The unified timeline + stats are built by the shared service (also feeds the Home spine).
        var timeline = await _timeline.GetTimelineAsync(userId, RangeStart, RangeEnd);
        Items = timeline.Items.ToList();
        Stats = timeline.Stats;

        return Page();
    }
}
