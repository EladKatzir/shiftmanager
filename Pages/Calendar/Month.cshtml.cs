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
/// ✅ PHASE 20: Monthly calendar view - Read-only unified view of shifts, chores, and on-duty
/// ✅ A-018: Scope switcher integration for filtering by mine/company/molecule/area
/// </summary>
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires [Authorize];
// data is re-scoped by Molecule/Area/JobType via scope switcher; OnDuty is global by design;
// hierarchy lookups (Companies, Molecules, JobTypes, ShiftGroupings) are reference data for filters
[Authorize]
public class MonthModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ICompanyContext _companyContext;
    private readonly ILogger<MonthModel> _logger;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly IDirectorService _directorService;
    private readonly IUserPreferenceService _userPreferenceService;
    private readonly IChoreService _choreService;
    private readonly IScopeFilterService _scopeFilterService;
    private readonly IGrantService _grantService;
    private readonly IJobTypeService _jobTypeService;

    public MonthModel(
        AppDbContext db,
        ICompanyContext companyContext,
        ILogger<MonthModel> logger,
        IStringLocalizer<SharedResources> localizer,
        IDirectorService directorService,
        IUserPreferenceService userPreferenceService,
        IChoreService choreService,
        IScopeFilterService scopeFilterService,
        IGrantService grantService,
        IJobTypeService jobTypeService)
    {
        _db = db;
        _companyContext = companyContext;
        _logger = logger;
        _localizer = localizer;
        _directorService = directorService;
        _userPreferenceService = userPreferenceService;
        _choreService = choreService;
        _scopeFilterService = scopeFilterService;
        _grantService = grantService;
        _jobTypeService = jobTypeService;
    }

    public DateOnly CurrentMonth { get; set; }
    public (DateOnly MonthYear, string Label) Previous { get; set; }
    public (DateOnly MonthYear, string Label) Next { get; set; }
    public List<List<DayVM>> Weeks { get; set; } = new();

    /// <summary>
    /// Start date of the calendar grid (first visible date, may be in previous month)
    /// Used for schedule export functionality
    /// </summary>
    public DateOnly StartDate { get; set; }

    /// <summary>
    /// End date of the calendar grid (last visible date, may be in next month)
    /// Used for schedule export functionality
    /// </summary>
    public DateOnly EndDate { get; set; }
    public bool ShowMyItemsOnly { get; set; }
    public int CurrentUserId { get; set; }

    // v3.0: Hierarchy filters
    public int? FilterJobTypeId { get; set; }
    public int? FilterShiftGroupingId { get; set; }
    public List<(int Id, string Name)> AvailableJobTypes { get; set; } = new();
    public List<(int Id, string Name)> AvailableShiftGroupings { get; set; } = new();

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

    // Grant-based access flag (set in OnGetAsync)
    public bool HasAdminAccess { get; set; }

    public class DayVM
    {
        public DateOnly Date { get; set; }
        public List<CalendarItemViewModel> Items { get; set; } = new();
    }

    public async Task OnGetAsync(int? year, int? month, int? jobTypeId = null, int? shiftGroupingId = null)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        DateOnly target;

        // Validate and parse date parameters with proper boundary handling
        if (year.HasValue && month.HasValue)
        {
            target = TryCreateValidMonth(year.Value, month.Value) ?? new DateOnly(today.Year, today.Month, 1);
        }
        else
        {
            target = new DateOnly(today.Year, today.Month, 1);
        }
        CurrentMonth = target;

        Previous = (target.AddMonths(-1), target.AddMonths(-1).ToString("MMM yyyy"));
        Next = (target.AddMonths(1), target.AddMonths(1).ToString("MMM yyyy"));

        // Get current user
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
        {
            _logger.LogError("Invalid or missing NameIdentifier claim");
            return;
        }
        CurrentUserId = currentUserId;

        // Grant-based access check (never use User.IsInRole)
        HasAdminAccess = await _grantService.HasGrantAsync(currentUserId, "AccessAdminNavigation");

        // ✅ A-018: Get scope from URL/cookie and resolve company IDs for filtering
        var (scopeType, scopeId) = _scopeFilterService.GetCurrentScope("shifts");
        
        // Validate user has access to the requested scope
        if (!await _scopeFilterService.ValidateScopeAccessAsync(scopeType, scopeId, "shifts"))
        {
            _logger.LogWarning("User {UserId} does not have access to scope {ScopeType}", currentUserId, scopeType);
            scopeType = "company";
            scopeId = null;
        }

        // Resolve scope to company IDs for data filtering
        var companyIds = await _scopeFilterService.ResolveCompanyIdsForScopeAsync(scopeType, scopeId);
        
        // Handle edge case: no companies for scope
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

        // v3.0: Apply hierarchy filters
        FilterJobTypeId = jobTypeId;
        FilterShiftGroupingId = shiftGroupingId;

        // v3.0: Load filter options from current user's area (use first company for context)
        var primaryCompanyId = companyIds.FirstOrDefault();
        if (primaryCompanyId > 0)
        {
            var company = await _db.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == primaryCompanyId);
            if (company?.MoleculeId != null)
            {
                var molecule = await _db.Molecules.IgnoreQueryFilters()
                    .Include(m => m.Area)
                    .FirstOrDefaultAsync(m => m.Id == company.MoleculeId);

                if (molecule?.AreaId != null)
                {
                    var jobTypesForArea = await _jobTypeService.GetJobTypesAsync(molecule.AreaId);
                    AvailableJobTypes = jobTypesForArea
                        .Select(jt => ValueTuple.Create(jt.Id, jt.DisplayName))
                        .ToList();
                }

                AvailableShiftGroupings = await _db.ShiftGroupings.IgnoreQueryFilters()
                    .Where(sg => sg.MoleculeId == company.MoleculeId && sg.IsActive)
                    .OrderBy(sg => sg.Name)
                    .Select(sg => ValueTuple.Create(sg.Id, sg.DisplayName))
                    .ToListAsync();
            }
        }

        _logger.LogInformation("✅ A-018: Month calendar for User {UserId}, Scope={Scope}, CompanyIds=[{CompanyIds}], ShowMyItemsOnly={ShowMyItemsOnly}",
            currentUserId, scopeType, string.Join(",", companyIds), ShowMyItemsOnly);

        var currentUser = await _db.Users.FindAsync(currentUserId);
        if (currentUser == null)
        {
            _logger.LogError("User {UserId} not found in database", currentUserId);
            return;
        }

        // Build 6-week grid starting Sunday
        var start = new DateOnly(target.Year, target.Month, 1);
        int delta = (int)start.DayOfWeek; // Days from Sunday (Sunday = 0)
        var gridStart = start.AddDays(-delta);
        var dates = Enumerable.Range(0, 42).Select(i => gridStart.AddDays(i)).ToList();

        // Set start and end dates for export functionality
        StartDate = dates.First();
        EndDate = dates.Last();

        // ✅ A-018: Load calendar items using scope-filtered company IDs
        var shifts = await LoadShiftsAsync(companyIds, dates, currentUserId);
        var chores = await LoadChoresAsync(companyIds, dates, currentUserId);
        var onDuties = await LoadOnDutiesAsync(companyIds, dates, currentUserId);

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

        // Build week grid
        for (int w = 0; w < 6; w++)
        {
            var week = new List<DayVM>();
            for (int d = 0; d < 7; d++)
            {
                var date = dates[w * 7 + d];
                var vm = new DayVM
                {
                    Date = date,
                    Items = itemsByDate.ContainsKey(date) ? itemsByDate[date] : new List<CalendarItemViewModel>()
                };
                week.Add(vm);
            }
            Weeks.Add(week);
        }

        // ✅ PHASE 7: Calculate header metrics after Weeks population
        CalculateHeaderMetrics();

        // ✅ PHASE 20: Load dropdown data for quick-add (users with AccessAdminNavigation grant)
        if (HasAdminAccess)
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

        if (!companyIds.Any())
            return items;

        // IgnoreQueryFilters: companyIds already scoped by ScopeFilterService —
        // EF tenant filter would restrict to single company, breaking molecule/area scopes
        var instancesQuery = _db.ShiftInstances
            .IgnoreQueryFilters()
            .Include(si => si.ShiftType)
            .Where(si => companyIds.Contains(si.CompanyId) && si.WorkDate >= dates.First() && si.WorkDate <= dates.Last());

        // v3.0: Apply JobType filter
        if (FilterJobTypeId.HasValue)
        {
            instancesQuery = instancesQuery.Where(si => si.ShiftType.JobTypeId == FilterJobTypeId.Value);
        }

        // v3.0: Apply ShiftGrouping filter
        if (FilterShiftGroupingId.HasValue)
        {
            instancesQuery = instancesQuery.Where(si => si.ShiftType.ShiftGroupingId == FilterShiftGroupingId.Value);
        }

        var instances = await instancesQuery.ToListAsync();

        var instanceIds = instances.Select(i => i.Id).ToList();

        // IgnoreQueryFilters on both tables: assignments and users may belong to different companies
        var assignments = await (from a in _db.ShiftAssignments.IgnoreQueryFilters()
                                join u in _db.Users.IgnoreQueryFilters() on a.UserId equals u.Id
                                where instanceIds.Contains(a.ShiftInstanceId)
                                select new { a.ShiftInstanceId, a.UserId, UserName = u.DisplayName, a.TraineeUserId })
                                .ToListAsync();

        // Group assignments by shift instance
        var assignmentsByInstance = assignments.GroupBy(a => a.ShiftInstanceId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Convert to CalendarItemViewModel
        foreach (var instance in instances)
        {
            // Create one item per assignment
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
                // Show unstaffed shifts to managers
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
        if (!companyIds.Any())
            return new List<CalendarItemViewModel>();

        // IgnoreQueryFilters: companyIds already scoped — tenant filter would break multi-company views
        var chores = await _db.Chores
            .IgnoreQueryFilters()
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
            TimeRange = "", // Chores don't have time ranges
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

        // Load custom types for icon/name mapping - OnDutyTypeConfig is also global
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
                TimeRange = "", // On-duty doesn't have time ranges
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
        // Phase 8.3: Fixed localization keys (removed underscores to match resource files)
        if (type == OnDutyType.Hakam)
        {
            return (_localizer["OnDutyHakam"].Value, "🛡️", "onduty-hakam");
        }
        else if (type == OnDutyType.Lead)
        {
            return (_localizer["OnDutyLead"].Value, "⭐", "onduty-lead");
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
        var allItems = Weeks.SelectMany(w => w).SelectMany(d => d.Items).ToList();

        VisibleShiftCount = allItems.Count(i => i.Type == CalendarItemType.Shift);
        VisibleChoreCount = allItems.Count(i => i.Type == CalendarItemType.Chore);
        VisibleOnDutyCount = allItems.Count(i => i.Type == CalendarItemType.OnDuty);
        TotalItemCount = allItems.Count;

        // Pending = unfilled shift slots
        PendingItemsCount = allItems
            .Where(i => i.Type == CalendarItemType.Shift && !string.IsNullOrEmpty(i.StaffingInfo))
            .Count(i => {
                var parts = i.StaffingInfo!.Split('/');
                return parts.Length == 2 &&
                       int.TryParse(parts[0], out var filled) &&
                       int.TryParse(parts[1], out var total) &&
                       filled < total;
            });

        // Coverage % (users with AccessAdminNavigation grant only)
        if (HasAdminAccess)
        {
            var shiftsWithStaffing = allItems
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
    /// ✅ A-019: Safely creates a valid DateOnly for the first day of a month, handling edge cases like:
    /// - Year boundaries (Dec -> Jan)
    /// - Invalid month/year values
    /// Returns null if the date cannot be created.
    /// </summary>
    private static DateOnly? TryCreateValidMonth(int year, int month)
    {
        // Validate year range (reasonable bounds for a scheduling app)
        if (year < 1900 || year > 2100)
            return null;

        // Validate month range
        if (month < 1 || month > 12)
            return null;

        try
        {
            return new DateOnly(year, month, 1);
        }
        catch (ArgumentOutOfRangeException)
        {
            // Final safety catch for any edge cases we missed
            return null;
        }
    }
}
