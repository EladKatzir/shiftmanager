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
/// ✅ PHASE 20: Daily calendar view - Read-only unified view of shifts, chores, and on-duty
/// </summary>
[Authorize]
public class DayModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ICompanyContext _companyContext;
    private readonly ILogger<DayModel> _logger;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly IUserPreferenceService _userPreferenceService;
    private readonly IChoreService _choreService;

    public DayModel(
        AppDbContext db,
        ICompanyContext companyContext,
        ILogger<DayModel> logger,
        IStringLocalizer<SharedResources> localizer,
        IUserPreferenceService userPreferenceService,
        IChoreService choreService)
    {
        _db = db;
        _companyContext = companyContext;
        _logger = logger;
        _localizer = localizer;
        _userPreferenceService = userPreferenceService;
        _choreService = choreService;
    }

    public DateOnly CurrentDate { get; set; }
    public (DateOnly Date, string Label) Previous { get; set; }
    public (DateOnly Date, string Label) Next { get; set; }
    public List<CalendarItemViewModel> Items { get; set; } = new();
    public bool ShowMyItemsOnly { get; set; }
    public int CurrentUserId { get; set; }

    // ✅ PHASE 20: Dropdown data for quick-add functionality
    public List<AppUser> EligibleAssignees { get; set; } = new();
    public List<OnDutyTypeConfig> CustomOnDutyTypes { get; set; } = new();

    public async Task OnGetAsync(int? year, int? month, int? day)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var target = year.HasValue && month.HasValue && day.HasValue
            ? new DateOnly(year.Value, month.Value, day.Value)
            : today;

        CurrentDate = target;
        Previous = (target.AddDays(-1), target.AddDays(-1).ToString("MMM dd, yyyy"));
        Next = (target.AddDays(1), target.AddDays(1).ToString("MMM dd, yyyy"));

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
        _logger.LogInformation("✅ PHASE 20: Day calendar for User {UserId}, CompanyId={CompanyId}, Date={Date}, ShowMyItemsOnly={ShowMyItemsOnly}",
            currentUserId, companyId, target, ShowMyItemsOnly);

        // Single-day list
        var dates = new List<DateOnly> { target };

        // ✅ PHASE 20: Load all three types of calendar items
        var shifts = await LoadShiftsAsync(companyId, dates, currentUserId);
        var chores = await LoadChoresAsync(companyId, dates, currentUserId);
        var onDuties = await LoadOnDutiesAsync(companyId, dates, currentUserId);

        // Combine all items
        Items = new List<CalendarItemViewModel>();
        Items.AddRange(shifts);
        Items.AddRange(chores);
        Items.AddRange(onDuties);

        // Apply "My Items Only" filter if enabled
        if (ShowMyItemsOnly)
        {
            Items = Items.Where(item => item.IsCurrentUser).ToList();
        }

        // Sort items by time (shifts first with time, then chores/onduty)
        Items = Items.OrderBy(item => item.TimeRange).ThenBy(item => item.Type).ToList();

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
}
