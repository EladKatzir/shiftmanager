using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Models.ViewModels;
using ShiftManager.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Localization;
using ShiftManager.Resources;
using System.Security.Claims;

namespace ShiftManager.Pages.Calendar;

/// <summary>
/// ✅ PHASE 20: Weekly calendar view - Read-only unified view of shifts, chores, and on-duty
/// </summary>
[Authorize]
public class WeekModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ICompanyContext _companyContext;
    private readonly ILogger<WeekModel> _logger;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly IDirectorService _directorService;
    private readonly IUserPreferenceService _userPreferenceService;
    private readonly IChoreService _choreService;

    public WeekModel(
        AppDbContext db,
        ICompanyContext companyContext,
        ILogger<WeekModel> logger,
        IStringLocalizer<SharedResources> localizer,
        IDirectorService directorService,
        IUserPreferenceService userPreferenceService,
        IChoreService choreService)
    {
        _db = db;
        _companyContext = companyContext;
        _logger = logger;
        _localizer = localizer;
        _directorService = directorService;
        _userPreferenceService = userPreferenceService;
        _choreService = choreService;
    }

    public DateOnly CurrentWeekStart { get; set; }
    public (DateOnly WeekStart, string Label) Previous { get; set; }
    public (DateOnly WeekStart, string Label) Next { get; set; }
    public List<DayVM> Days { get; set; } = new();
    public bool ShowMyItemsOnly { get; set; }
    public int CurrentUserId { get; set; }

    // ✅ PHASE 20: Dropdown data for quick-add functionality
    public List<AppUser> EligibleAssignees { get; set; } = new();
    public List<OnDutyTypeConfig> CustomOnDutyTypes { get; set; } = new();

    // ✅ PHASE 7: Calendar header contextual metrics
    public int VisibleShiftCount { get; set; }
    public int VisibleChoreCount { get; set; }
    public int VisibleOnDutyCount { get; set; }
    public int TotalItemCount { get; set; }
    public int PendingItemsCount { get; set; }
    public double CoveragePercent { get; set; }

    public class DayVM
    {
        public DateOnly Date { get; set; }
        public string DayName { get; set; } = "";
        public List<CalendarItemViewModel> Items { get; set; } = new();
    }

    public async Task OnGetAsync(int? year, int? month, int? day)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var target = year.HasValue && month.HasValue && day.HasValue
            ? new DateOnly(year.Value, month.Value, day.Value)
            : today;

        // Find Sunday of week
        int daysFromSunday = (int)target.DayOfWeek; // Sunday = 0
        var weekStart = target.AddDays(-daysFromSunday);
        CurrentWeekStart = weekStart;

        Previous = (weekStart.AddDays(-7), weekStart.AddDays(-7).ToString("MMM dd, yyyy"));
        Next = (weekStart.AddDays(7), weekStart.AddDays(7).ToString("MMM dd, yyyy"));

        // Get current user
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
        {
            _logger.LogError("Invalid or missing NameIdentifier claim");
            return;
        }
        CurrentUserId = currentUserId;

        // Get user preference for filtering
        ShowMyItemsOnly = _userPreferenceService.GetShowMyItemsOnly();

        var companyId = _companyContext.GetCompanyIdOrThrow();
        _logger.LogInformation("✅ PHASE 20: Week calendar for User {UserId}, CompanyId={CompanyId}, ShowMyItemsOnly={ShowMyItemsOnly}",
            currentUserId, companyId, ShowMyItemsOnly);

        // Build 7-day list
        var dates = Enumerable.Range(0, 7).Select(i => weekStart.AddDays(i)).ToList();

        // ✅ PHASE 20: Load all three types of calendar items
        var shifts = await LoadShiftsAsync(companyId, dates, currentUserId);
        var chores = await LoadChoresAsync(companyId, dates, currentUserId);
        var onDuties = await LoadOnDutiesAsync(companyId, dates, currentUserId);

        // Combine all items
        var allItems = new List<CalendarItemViewModel>();
        allItems.AddRange(shifts);
        allItems.AddRange(chores);
        allItems.AddRange(onDuties);

        // Apply "My Items Only" filter if enabled
        if (ShowMyItemsOnly)
        {
            allItems = allItems.Where(item => item.IsCurrentUser).ToList();
        }

        // Group items by date
        var itemsByDate = allItems.GroupBy(item => item.Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Build day list with day names
        foreach (var date in dates)
        {
            Days.Add(new DayVM
            {
                Date = date,
                DayName = date.ToString("dddd"),
                Items = itemsByDate.ContainsKey(date) ? itemsByDate[date] : new List<CalendarItemViewModel>()
            });
        }

        // ✅ PHASE 7: Calculate header metrics after Days population
        CalculateHeaderMetrics();

        // ✅ PHASE 20: Load dropdown data for quick-add (managers only)
        if (User.IsInRole("Manager") || User.IsInRole("Director") || User.IsInRole("Owner"))
        {
            EligibleAssignees = await _choreService.GetEligibleAssigneesAsync();

            // OnDutyTypeConfig is global - no company filter needed
            CustomOnDutyTypes = await _db.OnDutyTypeConfigs
                .Where(t => t.IsActive)
                .OrderBy(t => t.TypeValue)
                .ToListAsync();
        }
    }

    // ✅ PHASE 20: Reuse helper methods from Month (copied to avoid code duplication)
    private async Task<List<CalendarItemViewModel>> LoadShiftsAsync(int companyId, List<DateOnly> dates, int currentUserId)
    {
        var items = new List<CalendarItemViewModel>();

        var instances = await _db.ShiftInstances
            .Include(si => si.ShiftType)
            .Where(si => si.CompanyId == companyId && si.WorkDate >= dates.First() && si.WorkDate <= dates.Last())
            .ToListAsync();

        var instanceIds = instances.Select(i => i.Id).ToList();

        var assignments = await (from a in _db.ShiftAssignments
                                join u in _db.Users on a.UserId equals u.Id
                                where instanceIds.Contains(a.ShiftInstanceId)
                                select new { a.ShiftInstanceId, a.UserId, UserName = u.DisplayName, a.TraineeUserId })
                                .ToListAsync();

        var assignmentsByInstance = assignments.GroupBy(a => a.ShiftInstanceId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var instance in instances)
        {
            if (assignmentsByInstance.TryGetValue(instance.Id, out var shiftAssignments))
            {
                foreach (var assignment in shiftAssignments)
                {
                    var isCurrentUser = assignment.UserId == currentUserId;
                    var isTrainee = assignment.TraineeUserId.HasValue && assignment.TraineeUserId.Value == currentUserId;

                    items.Add(new CalendarItemViewModel
                    {
                        Type = CalendarItemType.Shift,
                        Id = instance.Id,
                        EntityId = instance.Id,
                        Date = instance.WorkDate,
                        Title = instance.ShiftType.Name,
                        AssigneeName = assignment.UserName,
                        TimeRange = $"{instance.ShiftType.Start:HH:mm} - {instance.ShiftType.End:HH:mm}",
                        ColorClass = $"shift-{instance.ShiftType.Key.ToLower()}",
                        Icon = GetShiftIcon(instance.ShiftType.Key),
                        IsCurrentUser = isCurrentUser || isTrainee,
                        ManagementUrl = $"/Calendar/Table?date={instance.WorkDate:yyyy-MM-dd}",
                        Details = string.IsNullOrEmpty(instance.Name) ? instance.ShiftType.Name : instance.Name,
                        StaffingInfo = $"{shiftAssignments.Count}/{instance.StaffingRequired}",
                        IsTrainee = isTrainee
                    });
                }
            }
            else if (instance.StaffingRequired > 0)
            {
                items.Add(new CalendarItemViewModel
                {
                    Type = CalendarItemType.Shift,
                    Id = instance.Id,
                    EntityId = instance.Id,
                    Date = instance.WorkDate,
                    Title = instance.ShiftType.Name,
                    AssigneeName = _localizer["Unassigned"].Value,
                    TimeRange = $"{instance.ShiftType.Start:HH:mm} - {instance.ShiftType.End:HH:mm}",
                    ColorClass = $"shift-{instance.ShiftType.Key.ToLower()}",
                    Icon = GetShiftIcon(instance.ShiftType.Key),
                    IsCurrentUser = false,
                    ManagementUrl = $"/Calendar/Table?date={instance.WorkDate:yyyy-MM-dd}",
                    Details = instance.ShiftType.Name,
                    StaffingInfo = $"0/{instance.StaffingRequired}"
                });
            }
        }

        return items;
    }

    private async Task<List<CalendarItemViewModel>> LoadChoresAsync(int companyId, List<DateOnly> dates, int currentUserId)
    {
        var chores = await _db.Chores
            .Include(c => c.User)
            .Where(c => c.CompanyId == companyId
                     && c.Date >= dates.First()
                     && c.Date <= dates.Last()
                     && c.CanceledAt == null)
            .ToListAsync();

        return chores.Select(chore => new CalendarItemViewModel
        {
            Type = CalendarItemType.Chore,
            Id = chore.Id,
            EntityId = chore.Id,
            Date = chore.Date,
            Title = chore.Title,
            AssigneeName = chore.User?.DisplayName ?? "Unknown",
            TimeRange = "",
            ColorClass = "chore-green",
            Icon = "🧹",
            IsCurrentUser = chore.UserId == currentUserId,
            ManagementUrl = $"/Public/Chores?year={chore.Date.Year}&month={chore.Date.Month}",
            Details = string.IsNullOrEmpty(chore.Notes) ? chore.Title : $"{chore.Title} - {chore.Notes}"
        }).ToList();
    }

    private async Task<List<CalendarItemViewModel>> LoadOnDutiesAsync(int companyId, List<DateOnly> dates, int currentUserId)
    {
        // OnDuty is global - must use IgnoreQueryFilters
        var onDuties = await _db.OnDuties.IgnoreQueryFilters()
            .Include(od => od.User)
            .Where(od => od.Date >= dates.First()
                      && od.Date <= dates.Last()
                      && od.CanceledAt == null)
            .ToListAsync();

        // OnDutyTypeConfig is also global
        var customTypes = await _db.OnDutyTypeConfigs
            .Where(t => t.IsActive)
            .ToListAsync();

        var customTypeDict = customTypes.ToDictionary(t => t.TypeValue, t => t);

        return onDuties.Select(onDuty =>
        {
            var (typeName, icon, colorClass) = GetOnDutyTypeInfo(onDuty.Type, customTypeDict);

            return new CalendarItemViewModel
            {
                Type = CalendarItemType.OnDuty,
                Id = onDuty.Id,
                EntityId = onDuty.Id,
                Date = onDuty.Date,
                Title = typeName,
                AssigneeName = onDuty.User?.DisplayName ?? "Unknown",
                TimeRange = "",
                ColorClass = colorClass,
                Icon = icon,
                IsCurrentUser = onDuty.UserId == currentUserId,
                ManagementUrl = $"/Public/OnDuty?year={onDuty.Date.Year}&month={onDuty.Date.Month}",
                Details = string.IsNullOrEmpty(onDuty.Notes) ? typeName : $"{typeName} - {onDuty.Notes}"
            };
        }).ToList();
    }

    private string GetShiftIcon(string shiftKey)
    {
        return shiftKey.ToLower() switch
        {
            "morning" => "🌅",
            "middle" => "☀️",
            "noon" => "🌤️",
            "night" => "🌙",
            _ => "📋"
        };
    }

    private (string Name, string Icon, string ColorClass) GetOnDutyTypeInfo(
        OnDutyType type,
        Dictionary<int, OnDutyTypeConfig> customTypes)
    {
        if (type == OnDutyType.Hakam)
        {
            return (_localizer["OnDuty_Hakam"].Value, "🛡️", "onduty-hakam");
        }
        else if (type == OnDutyType.Lead)
        {
            return (_localizer["OnDuty_Lead"].Value, "⭐", "onduty-lead");
        }
        else if (customTypes.ContainsKey((int)type))
        {
            var customType = customTypes[(int)type];
            var cultureName = System.Globalization.CultureInfo.CurrentUICulture.Name;
            var name = cultureName.StartsWith("he") ? customType.NameHe : customType.NameEn;
            return (name, customType.Icon, $"onduty-custom-{(int)type}");
        }
        else
        {
            return ("On-Duty", "📋", "onduty-default");
        }
    }

    /// <summary>
    /// ✅ PHASE 7: Calculate contextual metrics for calendar header
    /// </summary>
    private void CalculateHeaderMetrics()
    {
        var allItems = Days.SelectMany(d => d.Items).ToList();

        VisibleShiftCount = allItems.Count(i => i.Type == CalendarItemType.Shift);
        VisibleChoreCount = allItems.Count(i => i.Type == CalendarItemType.Chore);
        VisibleOnDutyCount = allItems.Count(i => i.Type == CalendarItemType.OnDuty);
        TotalItemCount = allItems.Count;

        // Pending = unfilled shift slots
        PendingItemsCount = allItems
            .Where(i => i.Type == CalendarItemType.Shift && !string.IsNullOrEmpty(i.StaffingInfo))
            .Count(i => {
                var parts = i.StaffingInfo.Split('/');
                return parts.Length == 2 &&
                       int.TryParse(parts[0], out var filled) &&
                       int.TryParse(parts[1], out var total) &&
                       filled < total;
            });

        // Coverage % (admin only)
        if (User.IsInRole("Owner") || User.IsInRole("Manager") || User.IsInRole("Director"))
        {
            var shiftsWithStaffing = allItems
                .Where(i => i.Type == CalendarItemType.Shift && !string.IsNullOrEmpty(i.StaffingInfo))
                .ToList();

            if (shiftsWithStaffing.Any())
            {
                int totalSlots = 0, filledSlots = 0;
                foreach (var shift in shiftsWithStaffing)
                {
                    var parts = shift.StaffingInfo.Split('/');
                    if (parts.Length == 2 &&
                        int.TryParse(parts[0], out var filled) &&
                        int.TryParse(parts[1], out var total))
                    {
                        totalSlots += total;
                        filledSlots += filled;
                    }
                }
                CoveragePercent = totalSlots > 0 ? Math.Round((double)filledSlots / totalSlots * 100, 1) : 100.0;
            }
            else
            {
                CoveragePercent = 100.0;
            }
        }
    }
}
