using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Pages.My;

[Authorize]
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    public enum ItemType { Vacation, OnDuty, Shift, Chore }
    public enum ViewMode { Upcoming, All, Past30Days }
    public enum TimeRange { Week, Month, Custom }

    public record TimelineItem(
        int Id,
        ItemType Type,
        DateOnly Date,
        DateOnly? EndDate,
        string Title,
        string? TimeRange,
        string? Metadata,
        bool HasConflict,
        double Hours,
        string? SubType = null
    );

    public record StatsData(
        int VacationCount, double VacationHours,
        int OnDutyCount, double OnDutyHours,
        int ShiftCount, double ShiftHours,
        int ChoreCount, double ChoreHours
    );

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

        var currentUser = await _db.Users.FindAsync(userId);
        if (currentUser == null)
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

        var items = new List<TimelineItem>();

        // 1. Load Shifts (regular + shadowing for trainees)
        var shifts = await (from a in _db.ShiftAssignments
                           join si in _db.ShiftInstances on a.ShiftInstanceId equals si.Id
                           join st in _db.ShiftTypes on si.ShiftTypeId equals st.Id
                           join t in _db.Users on a.TraineeUserId equals t.Id into traineeJoin
                           from trainee in traineeJoin.DefaultIfEmpty()
                           where a.UserId == userId && si.WorkDate >= RangeStart && si.WorkDate <= RangeEnd
                           select new {
                               si.Id,
                               si.WorkDate,
                               st.Key,
                               st.Name,
                               st.Start,
                               st.End,
                               TraineeName = trainee != null ? trainee.DisplayName : null,
                               InstanceName = si.Name
                           }).ToListAsync();

        foreach (var shift in shifts)
        {
            var hours = TimeHelpers.Hours(new ShiftType { Start = shift.Start, End = shift.End });
            var title = string.IsNullOrEmpty(shift.InstanceName)
                ? (shift.Key.StartsWith("CUSTOM_") ? shift.Name : shift.Key)
                : shift.InstanceName;
            var timeRange = $"{shift.Start:HH:mm} - {shift.End:HH:mm}";
            var metadata = shift.TraineeName != null ? $"Training: {shift.TraineeName}" : $"{hours:F1} hours";

            items.Add(new TimelineItem(
                shift.Id,
                ItemType.Shift,
                shift.WorkDate,
                null,
                $"Shift — {title}",
                timeRange,
                metadata,
                false,
                hours
            ));
        }

        // If user is trainee, load shadowing shifts
        if (currentUser.Role == UserRole.Trainee)
        {
            var shadowingShifts = await (from a in _db.ShiftAssignments
                                        join si in _db.ShiftInstances on a.ShiftInstanceId equals si.Id
                                        join st in _db.ShiftTypes on si.ShiftTypeId equals st.Id
                                        join u in _db.Users on a.UserId equals u.Id
                                        where a.TraineeUserId == userId && si.WorkDate >= RangeStart && si.WorkDate <= RangeEnd
                                        select new {
                                            si.Id,
                                            si.WorkDate,
                                            st.Key,
                                            st.Name,
                                            st.Start,
                                            st.End,
                                            EmployeeName = u.DisplayName,
                                            InstanceName = si.Name
                                        }).ToListAsync();

            foreach (var shadow in shadowingShifts)
            {
                var hours = TimeHelpers.Hours(new ShiftType { Start = shadow.Start, End = shadow.End });
                var title = string.IsNullOrEmpty(shadow.InstanceName)
                    ? (shadow.Key.StartsWith("CUSTOM_") ? shadow.Name : shadow.Key)
                    : shadow.InstanceName;
                var timeRange = $"{shadow.Start:HH:mm} - {shadow.End:HH:mm}";

                items.Add(new TimelineItem(
                    shadow.Id,
                    ItemType.Shift,
                    shadow.WorkDate,
                    null,
                    $"Shift — {title} (Shadowing)",
                    timeRange,
                    $"Shadowing: {shadow.EmployeeName}",
                    false,
                    hours,
                    "shadowing"
                ));
            }
        }

        // 2. Load Vacations (approved only)
        var vacations = await _db.TimeOffRequests
            .Where(r => r.UserId == userId &&
                       r.Status == RequestStatus.Approved &&
                       r.EndDate >= RangeStart &&
                       r.StartDate <= RangeEnd)
            .ToListAsync();

        foreach (var vacation in vacations)
        {
            var days = (vacation.EndDate.DayNumber - vacation.StartDate.DayNumber) + 1;
            var typeLabel = vacation.Type == TimeOffType.Vacation ? "Vacation" : "After";
            var metadata = days == 1 ? "1 day" : $"{days} days";

            items.Add(new TimelineItem(
                vacation.Id,
                ItemType.Vacation,
                vacation.StartDate,
                vacation.EndDate,
                typeLabel,
                null,
                metadata,
                false,
                0, // Vacations don't count as work hours
                vacation.Type.ToString()
            ));
        }

        // 3. Load On-Duty assignments (active only)
        var onDuties = await _db.OnDuties
            .Where(od => od.UserId == userId &&
                        od.CanceledAt == null &&
                        od.Date >= RangeStart &&
                        od.Date <= RangeEnd)
            .ToListAsync();

        foreach (var onDuty in onDuties)
        {
            var typeLabel = onDuty.Type == OnDutyType.Hakam ? "On-Duty Hakam" : "On-Duty Lead";
            var metadata = !string.IsNullOrEmpty(onDuty.Notes) ? onDuty.Notes : null;

            // On-duty typically adds extra hours
            var hours = 0.0; // Could be configured per type

            items.Add(new TimelineItem(
                onDuty.Id,
                ItemType.OnDuty,
                onDuty.Date,
                null,
                typeLabel,
                null,
                metadata,
                false,
                hours,
                onDuty.Type.ToString()
            ));
        }

        // 4. Load Chores (active only)
        var chores = await _db.Chores
            .Where(c => c.UserId == userId &&
                       c.CanceledAt == null &&
                       c.Date >= RangeStart &&
                       c.Date <= RangeEnd)
            .ToListAsync();

        foreach (var chore in chores)
        {
            items.Add(new TimelineItem(
                chore.Id,
                ItemType.Chore,
                chore.Date,
                null,
                $"Chore: {chore.Title}",
                null,
                chore.Notes,
                false,
                0, // Chores typically don't add work hours
                null
            ));
        }

        // Detect conflicts (items on same date)
        var itemsByDate = items.GroupBy(i => i.Date).Where(g => g.Count() > 1).ToList();
        if (itemsByDate.Any())
        {
            var conflictDates = itemsByDate.Select(g => g.Key).ToHashSet();
            items = items.Select(item => item with { HasConflict = conflictDates.Contains(item.Date) }).ToList();
        }

        // Sort by date
        Items = items.OrderBy(i => i.Date).ToList();

        // Calculate stats
        var vacationItems = items.Where(i => i.Type == ItemType.Vacation).ToList();
        var onDutyItems = items.Where(i => i.Type == ItemType.OnDuty).ToList();
        var shiftItems = items.Where(i => i.Type == ItemType.Shift).ToList();
        var choreItems = items.Where(i => i.Type == ItemType.Chore).ToList();

        Stats = new StatsData(
            vacationItems.Count,
            vacationItems.Sum(i => i.Hours),
            onDutyItems.Count,
            onDutyItems.Sum(i => i.Hours),
            shiftItems.Count,
            shiftItems.Sum(i => i.Hours),
            choreItems.Count,
            choreItems.Sum(i => i.Hours)
        );

        return Page();
    }
}
