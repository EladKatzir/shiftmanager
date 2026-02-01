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
/// ✅ A-018: Scope switcher integration for filtering by mine/company/molecule/area
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
    private readonly IScopeFilterService _scopeFilterService;

    public DayModel(
        AppDbContext db,
        ICompanyContext companyContext,
        ILogger<DayModel> logger,
        IStringLocalizer<SharedResources> localizer,
        IUserPreferenceService userPreferenceService,
        IChoreService choreService,
        IScopeFilterService scopeFilterService)
    {
        _db = db;
        _companyContext = companyContext;
        _logger = logger;
        _localizer = localizer;
        _userPreferenceService = userPreferenceService;
        _choreService = choreService;
        _scopeFilterService = scopeFilterService;
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

    // ✅ PHASE 7: Calendar header contextual metrics
    public int VisibleShiftCount { get; set; }
    public int VisibleChoreCount { get; set; }
    public int VisibleOnDutyCount { get; set; }
    public int TotalItemCount { get; set; }
    public int PendingItemsCount { get; set; }
    public double CoveragePercent { get; set; }

    public async Task OnGetAsync(int? year, int? month, int? day)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var target = today;

        // Validate and parse date parameters with proper boundary handling
        if (year.HasValue && month.HasValue && day.HasValue)
        {
            target = TryCreateValidDate(year.Value, month.Value, day.Value) ?? today;
        }

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

        // ✅ A-018: Get scope from URL/cookie and resolve company IDs for filtering
        var (scopeType, scopeId) = _scopeFilterService.GetCurrentScope("shifts");
        
        // Validate user has access to the requested scope
        if (!await _scopeFilterService.ValidateScopeAccessAsync(scopeType, scopeId, "shifts"))
        {
            _logger.LogWarning("User {UserId} does not have access to scope {ScopeType}", currentUserId, scopeType);
            // Fall back to company scope
            scopeType = "company";
            scopeId = null;
        }

        // Resolve scope to company IDs for data filtering
        var companyIds = await _scopeFilterService.ResolveCompanyIdsForScopeAsync(scopeType, scopeId);
        
        // Handle edge case: no companies for scope (fall back to user's company)
        if (!companyIds.Any())
        {
            var fallbackCompanyId = _companyContext.CompanyId;
            if (fallbackCompanyId.HasValue && fallbackCompanyId.Value > 0)
            {
                companyIds = new List<int> { fallbackCompanyId.Value };
            }
        }

        // Determine if we should filter to current user only
        ShowMyItemsOnly = _scopeFilterService.ShouldFilterToCurrentUserOnly(scopeType);

        _logger.LogInformation("✅ A-018: Day calendar for User {UserId}, Scope={Scope}, CompanyIds=[{CompanyIds}], Date={Date}, ShowMyItemsOnly={ShowMyItemsOnly}",
            currentUserId, scopeType, string.Join(",", companyIds), target, ShowMyItemsOnly);

        // Single-day list
        var dates = new List<DateOnly> { target };

        // ✅ A-018: Load calendar items using scope-filtered company IDs
        var shifts = await LoadShiftsAsync(companyIds, dates, currentUserId);
        var chores = await LoadChoresAsync(companyIds, dates, currentUserId);
        var onDuties = await LoadOnDutiesAsync(companyIds, dates, currentUserId);

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

        // ✅ PHASE 7: Calculate header metrics after Items population
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

    /// <summary>
    /// ✅ A-018: Load shifts for multiple company IDs (scope-aware)
    /// </summary>
    private async Task<List<CalendarItemViewModel>> LoadShiftsAsync(List<int> companyIds, List<DateOnly> dates, int currentUserId)
    {
        var items = new List<CalendarItemViewModel>();

        // Handle empty company list edge case
        if (!companyIds.Any())
            return items;

        var instances = await _db.ShiftInstances
            .Include(si => si.ShiftType)
            .Where(si => companyIds.Contains(si.CompanyId) && si.WorkDate >= dates.First() && si.WorkDate <= dates.Last())
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

    /// <summary>
    /// ✅ A-018: Load chores for multiple company IDs (scope-aware)
    /// </summary>
    private async Task<List<CalendarItemViewModel>> LoadChoresAsync(List<int> companyIds, List<DateOnly> dates, int currentUserId)
    {
        // Handle empty company list edge case
        if (!companyIds.Any())
            return new List<CalendarItemViewModel>();

        var chores = await _db.Chores
            .Include(c => c.User)
            .Where(c => companyIds.Contains(c.CompanyId)
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

    /// <summary>
    /// ✅ A-018: Load on-duties - global scope (not filtered by company)
    /// </summary>
    private async Task<List<CalendarItemViewModel>> LoadOnDutiesAsync(List<int> companyIds, List<DateOnly> dates, int currentUserId)
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
        VisibleShiftCount = Items.Count(i => i.Type == CalendarItemType.Shift);
        VisibleChoreCount = Items.Count(i => i.Type == CalendarItemType.Chore);
        VisibleOnDutyCount = Items.Count(i => i.Type == CalendarItemType.OnDuty);
        TotalItemCount = Items.Count;

        // Pending = unfilled shift slots
        PendingItemsCount = Items
            .Where(i => i.Type == CalendarItemType.Shift && !string.IsNullOrEmpty(i.StaffingInfo))
            .Count(i => {
                var parts = i.StaffingInfo!.Split('/');
                return parts.Length == 2 &&
                       int.TryParse(parts[0], out var filled) &&
                       int.TryParse(parts[1], out var total) &&
                       filled < total;
            });

        // Coverage % (admin only)
        if (User.IsInRole("Owner") || User.IsInRole("Manager") || User.IsInRole("Director"))
        {
            var shiftsWithStaffing = Items
                .Where(i => i.Type == CalendarItemType.Shift && !string.IsNullOrEmpty(i.StaffingInfo))
                .ToList();

            if (shiftsWithStaffing.Any())
            {
                int totalSlots = 0, filledSlots = 0;
                foreach (var shift in shiftsWithStaffing)
                {
                    var parts = shift.StaffingInfo!.Split('/');
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

    /// <summary>
    /// ✅ A-019: Safely creates a valid DateOnly from parameters, handling edge cases like:
    /// - Month boundaries (Jan 31 -> Feb navigation)
    /// - Year boundaries (Dec -> Jan)
    /// - Leap years (Feb 29)
    /// - Invalid date values
    /// Returns null if the date cannot be created.
    /// </summary>
    private static DateOnly? TryCreateValidDate(int year, int month, int day)
    {
        // Validate year range (reasonable bounds for a scheduling app)
        if (year < 1900 || year > 2100)
            return null;

        // Validate month range
        if (month < 1 || month > 12)
            return null;

        // Validate day range (accounting for varying days per month and leap years)
        if (day < 1)
            return null;

        // Get the actual number of days in the specified month/year
        int daysInMonth = DateTime.DaysInMonth(year, month);

        // If day exceeds valid range for this month, clamp to the last valid day
        // This handles cases like navigating from Jan 31 to Feb (which only has 28/29 days)
        if (day > daysInMonth)
            day = daysInMonth;

        try
        {
            return new DateOnly(year, month, day);
        }
        catch (ArgumentOutOfRangeException)
        {
            // Final safety catch for any edge cases we missed
            return null;
        }
    }
}
