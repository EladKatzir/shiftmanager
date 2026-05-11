using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ShiftManager.Data;
using ShiftManager.Models.Analytics;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

/// <summary>
/// Service interface for analytics and reporting
/// </summary>
public interface IAnalyticsService
{
    // Employee Analytics
    Task<List<EmployeeHoursDto>> GetEmployeeHoursAsync(DateOnly startDate, DateOnly endDate);
    Task<List<EmployeeShiftCountDto>> GetUpcomingShiftsAsync(int days = 7);
    Task<List<BackToBackShiftDto>> GetBackToBackShiftsAsync(int days = 30);

    // Team Analytics
    Task<Dictionary<UserRole, decimal>> GetHoursByRoleAsync(DateOnly startDate, DateOnly endDate);
    Task<List<StaffingIssueDto>> GetUnderstaffingReportAsync(DateOnly startDate, DateOnly endDate);
    Task<List<StaffingIssueDto>> GetOverstaffingReportAsync(DateOnly startDate, DateOnly endDate);
    Task<decimal> GetCoverageRateAsync(DateOnly startDate, DateOnly endDate);

    // Swap Analytics
    Task<SwapStatsDto> GetSwapStatsAsync(DateOnly startDate, DateOnly endDate);
    Task<List<TopSwapperDto>> GetTopSwappersAsync(int topN = 10, int days = 30);
    Task<TimeSpan> GetAverageSwapApprovalTimeAsync(int days = 30);

    // Time-Off Analytics
    Task<TimeOffStatsDto> GetTimeOffStatsAsync(DateOnly startDate, DateOnly endDate);
    Task<decimal> GetAverageDaysOffPerEmployeeAsync(int days = 30);
    Task<Dictionary<string, int>> GetTimeOffByMonthAsync(int months = 12);
}

/// <summary>
/// Service for generating workforce analytics and reports
/// </summary>
public class AnalyticsService : IAnalyticsService
{
    private readonly AppDbContext _db;
    private readonly ITenantResolver _tenantResolver;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AnalyticsService> _logger;
    private readonly ICompanyLocalizationService _localizationService;

    private const int CacheDurationMinutes = 5;

    public AnalyticsService(
        AppDbContext db,
        ITenantResolver tenantResolver,
        IMemoryCache cache,
        ILogger<AnalyticsService> logger,
        ICompanyLocalizationService localizationService)
    {
        _db = db;
        _tenantResolver = tenantResolver;
        _cache = cache;
        _logger = logger;
        _localizationService = localizationService;
    }

    // ==================== Employee Analytics ====================

    public async Task<List<EmployeeHoursDto>> GetEmployeeHoursAsync(DateOnly startDate, DateOnly endDate)
    {
        var cacheKey = $"Analytics_EmployeeHours_{_tenantResolver.GetCurrentTenantId()}_{startDate}_{endDate}";
        if (_cache.TryGetValue(cacheKey, out List<EmployeeHoursDto>? cached) && cached != null)
        {
            return cached;
        }

        try
        {
            // Phase 2C: Use SQL aggregates instead of loading all data into memory
            // Group by user first in the database, then load minimal data
            var groupedData = await _db.ShiftAssignments
                .AsNoTracking()
                .Include(a => a.ShiftInstance)
                .ThenInclude(si => si.ShiftType)
                .Include(a => a.User)
                .Where(a => a.ShiftInstance.WorkDate >= startDate && a.ShiftInstance.WorkDate <= endDate)
                .Where(a => a.UserId.HasValue) // Filter out unassigned slots
                .GroupBy(a => new { a.UserId, a.User!.DisplayName })
                .Select(g => new
                {
                    UserId = g.Key.UserId!.Value,
                    DisplayName = g.Key.DisplayName,
                    ShiftCount = g.Count(),
                    Shifts = g.Select(a => new
                    {
                        Start = a.ShiftInstance.ShiftType.Start,
                        End = a.ShiftInstance.ShiftType.End
                    }).ToList()
                })
                .ToListAsync();

            var weeks = (decimal)(endDate.DayNumber - startDate.DayNumber) / 7;

            var results = groupedData
                .Select(g =>
                {
                    var totalHours = g.Shifts.Sum(shift =>
                    {
                        var start = shift.Start;
                        var end = shift.End;

                        // Handle overnight shifts
                        if (end < start)
                        {
                            return (decimal)(24 - start.Hour + end.Hour + (end.Minute - start.Minute) / 60.0);
                        }
                        else
                        {
                            return (decimal)((end.Hour - start.Hour) + (end.Minute - start.Minute) / 60.0);
                        }
                    });

                    var avgHoursPerWeek = weeks > 0 ? totalHours / weeks : 0;

                    return new EmployeeHoursDto
                    {
                        UserId = g.UserId,
                        EmployeeName = g.DisplayName,
                        TotalHours = Math.Round(totalHours, 2),
                        AverageHoursPerWeek = Math.Round(avgHoursPerWeek, 2),
                        ShiftCount = g.ShiftCount
                    };
                })
                .OrderByDescending(e => e.TotalHours)
                .ToList();

            _cache.Set(cacheKey, results, TimeSpan.FromMinutes(CacheDurationMinutes));
            return results;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error calculating employee hours");
            return new List<EmployeeHoursDto>();
        }
    }

    public async Task<List<EmployeeShiftCountDto>> GetUpcomingShiftsAsync(int days = 7)
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var endDate = today.AddDays(days);

            // Phase 2C: Use SQL aggregates - GroupBy before ToListAsync
            var results = await _db.ShiftAssignments
                .AsNoTracking()
                .Include(a => a.User)
                .Where(a => a.ShiftInstance.WorkDate >= today && a.ShiftInstance.WorkDate < endDate)
                .Where(a => a.UserId.HasValue) // Filter out unassigned slots
                .GroupBy(a => new { a.UserId, a.User!.DisplayName })
                .Select(g => new EmployeeShiftCountDto
                {
                    UserId = g.Key.UserId!.Value,
                    EmployeeName = g.Key.DisplayName,
                    UpcomingShiftCount = g.Count()
                })
                .OrderByDescending(e => e.UpcomingShiftCount)
                .ToListAsync();

            return results;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error calculating upcoming shifts");
            return new List<EmployeeShiftCountDto>();
        }
    }

    public async Task<List<BackToBackShiftDto>> GetBackToBackShiftsAsync(int days = 30)
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var endDate = today.AddDays(days);

            var assignments = await _db.ShiftAssignments
                .AsNoTracking()
                .Include(a => a.ShiftInstance)
                .ThenInclude(si => si.ShiftType)
                .Include(a => a.User)
                .Where(a => a.ShiftInstance.WorkDate >= today && a.ShiftInstance.WorkDate < endDate)
                .Where(a => a.UserId.HasValue) // Filter out unassigned slots
                .OrderBy(a => a.UserId)
                .ThenBy(a => a.ShiftInstance.WorkDate)
                .ThenBy(a => a.ShiftInstance.ShiftType.Start)
                .ToListAsync();

            var warnings = new List<BackToBackShiftDto>();
            var companyId = _tenantResolver.GetCurrentTenantId();
            var culture = CultureInfo.CurrentUICulture.Name;

            // Group by user
            var userAssignments = assignments.GroupBy(a => new { a.UserId, a.User!.DisplayName });

            foreach (var userGroup in userAssignments)
            {
                var shifts = userGroup.OrderBy(a => a.ShiftInstance.WorkDate)
                    .ThenBy(a => a.ShiftInstance.ShiftType.Start)
                    .ToList();

                for (int i = 0; i < shifts.Count - 1; i++)
                {
                    var current = shifts[i];
                    var next = shifts[i + 1];

                    var currentEnd = current.ShiftInstance.ShiftType.End;
                    var nextStart = next.ShiftInstance.ShiftType.Start;

                    // Calculate hours between shifts
                    var currentDate = current.ShiftInstance.WorkDate;
                    var nextDate = next.ShiftInstance.WorkDate;

                    var currentEndDateTime = currentDate.ToDateTime(currentEnd);
                    var nextStartDateTime = nextDate.ToDateTime(nextStart);

                    // Handle overnight shifts
                    if (currentEnd < current.ShiftInstance.ShiftType.Start)
                    {
                        currentEndDateTime = currentEndDateTime.AddDays(1);
                    }

                    var restHours = (decimal)(nextStartDateTime - currentEndDateTime).TotalHours;

                    // Flag if rest is less than 8 hours
                    if (restHours < 8 && restHours >= 0)
                    {
                        var firstShiftTypeName = await _localizationService.ResolveShiftTypeNameAsync(current.ShiftInstance.ShiftType, companyId, culture);
                        var secondShiftTypeName = await _localizationService.ResolveShiftTypeNameAsync(next.ShiftInstance.ShiftType, companyId, culture);
                        warnings.Add(new BackToBackShiftDto
                        {
                            UserId = userGroup.Key.UserId!.Value,
                            EmployeeName = userGroup.Key.DisplayName,
                            FirstShiftDate = current.ShiftInstance.WorkDate,
                            FirstShiftType = firstShiftTypeName,
                            FirstShiftEnd = current.ShiftInstance.ShiftType.End,
                            SecondShiftDate = next.ShiftInstance.WorkDate,
                            SecondShiftType = secondShiftTypeName,
                            SecondShiftStart = next.ShiftInstance.ShiftType.Start,
                            RestHours = Math.Round(restHours, 1)
                        });
                    }
                }
            }

            return warnings.OrderBy(w => w.RestHours).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error calculating back-to-back shifts");
            return new List<BackToBackShiftDto>();
        }
    }

    // ==================== Team Analytics ====================

    public async Task<Dictionary<UserRole, decimal>> GetHoursByRoleAsync(DateOnly startDate, DateOnly endDate)
    {
        try
        {
            // Phase 2C: Use SQL aggregates - GroupBy before ToListAsync
            var groupedData = await _db.ShiftAssignments
                .AsNoTracking()
                .Include(a => a.ShiftInstance)
                .ThenInclude(si => si.ShiftType)
                .Include(a => a.User)
                .Where(a => a.ShiftInstance.WorkDate >= startDate && a.ShiftInstance.WorkDate <= endDate)
                .Where(a => a.User != null)  // Filter out unassigned shifts
                .GroupBy(a => a.User!.Role)
                .Select(g => new
                {
                    Role = g.Key,
                    Shifts = g.Select(a => new
                    {
                        Start = a.ShiftInstance.ShiftType.Start,
                        End = a.ShiftInstance.ShiftType.End
                    }).ToList()
                })
                .ToListAsync();

            var results = groupedData.ToDictionary(
                g => g.Role,
                g => g.Shifts.Sum(shift =>
                {
                    var start = shift.Start;
                    var end = shift.End;

                    // Handle overnight shifts
                    if (end < start)
                    {
                        return (decimal)(24 - start.Hour + end.Hour + (end.Minute - start.Minute) / 60.0);
                    }
                    else
                    {
                        return (decimal)((end.Hour - start.Hour) + (end.Minute - start.Minute) / 60.0);
                    }
                })
            );

            return results;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error calculating hours by role");
            return new Dictionary<UserRole, decimal>();
        }
    }

    public async Task<List<StaffingIssueDto>> GetUnderstaffingReportAsync(DateOnly startDate, DateOnly endDate)
    {
        try
        {
            var instances = await _db.ShiftInstances
                .AsNoTracking()
                .Include(si => si.ShiftType)
                .Where(si => si.WorkDate >= startDate && si.WorkDate <= endDate)
                .ToListAsync();

            // Get assignment counts grouped by shift instance
            var assignmentCounts = await _db.ShiftAssignments
                .AsNoTracking()
                .Where(a => a.ShiftInstance.WorkDate >= startDate && a.ShiftInstance.WorkDate <= endDate)
                .GroupBy(a => a.ShiftInstanceId)
                .Select(g => new { ShiftInstanceId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ShiftInstanceId, x => x.Count);

            var understaffedItems = instances
                .Select(si => new
                {
                    ShiftInstance = si,
                    AssignedCount = assignmentCounts.ContainsKey(si.Id) ? assignmentCounts[si.Id] : 0
                })
                .Where(x => x.AssignedCount < x.ShiftInstance.StaffingRequired)
                .ToList();

            var companyId = _tenantResolver.GetCurrentTenantId();
            var culture = CultureInfo.CurrentUICulture.Name;
            var results = new List<StaffingIssueDto>();
            foreach (var x in understaffedItems)
            {
                results.Add(new StaffingIssueDto
                {
                    WorkDate = x.ShiftInstance.WorkDate,
                    ShiftType = await _localizationService.ResolveShiftTypeNameAsync(x.ShiftInstance.ShiftType, companyId, culture),
                    AssignedCount = x.AssignedCount,
                    RequiredCount = x.ShiftInstance.StaffingRequired,
                    Difference = x.AssignedCount - x.ShiftInstance.StaffingRequired
                });
            }
            results = results.OrderBy(s => s.WorkDate).ThenBy(s => s.ShiftType).ToList();

            return results;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error calculating understaffing report");
            return new List<StaffingIssueDto>();
        }
    }

    public async Task<List<StaffingIssueDto>> GetOverstaffingReportAsync(DateOnly startDate, DateOnly endDate)
    {
        try
        {
            var instances = await _db.ShiftInstances
                .AsNoTracking()
                .Include(si => si.ShiftType)
                .Where(si => si.WorkDate >= startDate && si.WorkDate <= endDate)
                .ToListAsync();

            // Get assignment counts grouped by shift instance
            var assignmentCounts = await _db.ShiftAssignments
                .AsNoTracking()
                .Where(a => a.ShiftInstance.WorkDate >= startDate && a.ShiftInstance.WorkDate <= endDate)
                .GroupBy(a => a.ShiftInstanceId)
                .Select(g => new { ShiftInstanceId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ShiftInstanceId, x => x.Count);

            var overstaffedItems = instances
                .Select(si => new
                {
                    ShiftInstance = si,
                    AssignedCount = assignmentCounts.ContainsKey(si.Id) ? assignmentCounts[si.Id] : 0
                })
                .Where(x => x.AssignedCount > x.ShiftInstance.StaffingRequired)
                .ToList();

            var companyId = _tenantResolver.GetCurrentTenantId();
            var culture = CultureInfo.CurrentUICulture.Name;
            var results = new List<StaffingIssueDto>();
            foreach (var x in overstaffedItems)
            {
                results.Add(new StaffingIssueDto
                {
                    WorkDate = x.ShiftInstance.WorkDate,
                    ShiftType = await _localizationService.ResolveShiftTypeNameAsync(x.ShiftInstance.ShiftType, companyId, culture),
                    AssignedCount = x.AssignedCount,
                    RequiredCount = x.ShiftInstance.StaffingRequired,
                    Difference = x.AssignedCount - x.ShiftInstance.StaffingRequired
                });
            }
            results = results.OrderBy(s => s.WorkDate).ThenBy(s => s.ShiftType)
                .ToList();

            return results;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error calculating overstaffing report");
            return new List<StaffingIssueDto>();
        }
    }

    public async Task<decimal> GetCoverageRateAsync(DateOnly startDate, DateOnly endDate)
    {
        try
        {
            // Exclude all HOME variants and OFFLINE from coverage — they're not real operational shifts
            var exemptKeys = new[] {
                ShiftManager.Models.ShiftType.KEY_HOME,
                ShiftManager.Models.ShiftType.KEY_HOME_PM,
                ShiftManager.Models.ShiftType.KEY_HOME_AM,
                ShiftManager.Models.ShiftType.KEY_OFFLINE
            };
            var instances = await _db.ShiftInstances
                .AsNoTracking()
                .Include(si => si.ShiftType)
                .Where(si => si.WorkDate >= startDate && si.WorkDate <= endDate
                    && !exemptKeys.Contains(si.ShiftType.Key))
                .ToListAsync();

            if (!instances.Any())
            {
                return 0;
            }

            var totalRequired = instances.Sum(si => si.StaffingRequired);
            var instanceIds = instances.Select(si => si.Id).ToHashSet();

            var totalAssigned = await _db.ShiftAssignments
                .AsNoTracking()
                .Where(a => instanceIds.Contains(a.ShiftInstanceId)
                    && a.UserId != null)
                .CountAsync();

            if (totalRequired == 0)
            {
                return 0;
            }

            var coverageRate = (decimal)totalAssigned / totalRequired * 100;
            return Math.Round(Math.Min(coverageRate, 100), 2);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error calculating coverage rate");
            return 0;
        }
    }

    // ==================== Swap Analytics ====================

    public async Task<SwapStatsDto> GetSwapStatsAsync(DateOnly startDate, DateOnly endDate)
    {
        try
        {
            var startDateTime = startDate.ToDateTime(TimeOnly.MinValue);
            var endDateTime = endDate.ToDateTime(TimeOnly.MaxValue);

            var swaps = await _db.SwapRequests
                .AsNoTracking()
                .Where(sr => sr.CreatedAt >= startDateTime && sr.CreatedAt <= endDateTime)
                .ToListAsync();

            var totalRequests = swaps.Count;
            var approvedCount = swaps.Count(sr => sr.Status == RequestStatus.Approved);
            var declinedCount = swaps.Count(sr => sr.Status == RequestStatus.Declined);
            var pendingCount = swaps.Count(sr => sr.Status == RequestStatus.Pending);

            var approvalRate = totalRequests > 0 ? (decimal)approvedCount / totalRequests * 100 : 0;

            return new SwapStatsDto
            {
                TotalRequests = totalRequests,
                ApprovedCount = approvedCount,
                DeclinedCount = declinedCount,
                PendingCount = pendingCount,
                ApprovalRate = Math.Round(approvalRate, 2)
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error calculating swap stats");
            return new SwapStatsDto();
        }
    }

    public async Task<List<TopSwapperDto>> GetTopSwappersAsync(int topN = 10, int days = 30)
    {
        try
        {
            var startDate = DateTime.UtcNow.AddDays(-days);

            // Get swap requests with from assignments
            var swaps = await _db.SwapRequests
                .AsNoTracking()
                .Where(sr => sr.CreatedAt >= startDate)
                .Join(_db.ShiftAssignments.Include(a => a.User),
                      sr => sr.FromAssignmentId,
                      a => a.Id,
                      (sr, a) => new { SwapRequest = sr, User = a.User })
                .ToListAsync();

            var results = swaps
                .Where(x => x.User != null)  // Filter out null users
                .GroupBy(x => new { x.User!.Id, x.User.DisplayName })
                .Select(g => new TopSwapperDto
                {
                    UserId = g.Key.Id,
                    EmployeeName = g.Key.DisplayName,
                    SwapRequestCount = g.Count(),
                    ApprovedCount = g.Count(x => x.SwapRequest.Status == RequestStatus.Approved),
                    DeclinedCount = g.Count(x => x.SwapRequest.Status == RequestStatus.Declined)
                })
                .OrderByDescending(t => t.SwapRequestCount)
                .Take(topN)
                .ToList();

            return results;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error calculating top swappers");
            return new List<TopSwapperDto>();
        }
    }

    public Task<TimeSpan> GetAverageSwapApprovalTimeAsync(int days = 30)
    {
        try
        {
            // Note: SwapRequest model doesn't have ReviewedAt field
            // Return zero for now - this would need to be added to the model
            _logger.LogWarning("SwapRequest model does not have ReviewedAt field. Cannot calculate average approval time.");
            return Task.FromResult(TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error calculating average swap approval time");
            return Task.FromResult(TimeSpan.Zero);
        }
    }

    // ==================== Time-Off Analytics ====================

    public async Task<TimeOffStatsDto> GetTimeOffStatsAsync(DateOnly startDate, DateOnly endDate)
    {
        try
        {
            var startDateTime = startDate.ToDateTime(TimeOnly.MinValue);
            var endDateTime = endDate.ToDateTime(TimeOnly.MaxValue);

            var requests = await _db.TimeOffRequests
                .AsNoTracking()
                .Where(tor => tor.CreatedAt >= startDateTime && tor.CreatedAt <= endDateTime)
                .ToListAsync();

            var totalRequests = requests.Count;
            var approvedCount = requests.Count(r => r.Status == RequestStatus.Approved);
            var declinedCount = requests.Count(r => r.Status == RequestStatus.Declined);
            var pendingCount = requests.Count(r => r.Status == RequestStatus.Pending);

            var approvalRate = totalRequests > 0 ? (decimal)approvedCount / totalRequests * 100 : 0;

            return new TimeOffStatsDto
            {
                TotalRequests = totalRequests,
                ApprovedCount = approvedCount,
                DeclinedCount = declinedCount,
                PendingCount = pendingCount,
                ApprovalRate = Math.Round(approvalRate, 2)
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error calculating time-off stats");
            return new TimeOffStatsDto();
        }
    }

    public async Task<decimal> GetAverageDaysOffPerEmployeeAsync(int days = 30)
    {
        try
        {
            var startDate = DateTime.UtcNow.AddDays(-days);

            var approvedRequests = await _db.TimeOffRequests
                .AsNoTracking()
                .Where(tor => tor.Status == RequestStatus.Approved && tor.CreatedAt >= startDate)
                .ToListAsync();

            if (!approvedRequests.Any())
            {
                return 0;
            }

            var totalDays = approvedRequests.Sum(r => (r.EndDate.DayNumber - r.StartDate.DayNumber) + 1);
            var employeeCount = await _db.Users.CountAsync(u => u.IsActive);

            if (employeeCount == 0)
            {
                return 0;
            }

            return Math.Round((decimal)totalDays / employeeCount, 2);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error calculating average days off per employee");
            return 0;
        }
    }

    public async Task<Dictionary<string, int>> GetTimeOffByMonthAsync(int months = 12)
    {
        try
        {
            var startDate = DateTime.UtcNow.AddMonths(-months);

            var approvedRequests = await _db.TimeOffRequests
                .AsNoTracking()
                .Where(tor => tor.Status == RequestStatus.Approved && tor.CreatedAt >= startDate)
                .ToListAsync();

            var results = new Dictionary<string, int>();

            // Initialize all months with 0
            for (int i = months - 1; i >= 0; i--)
            {
                var date = DateTime.UtcNow.AddMonths(-i);
                var key = date.ToString("yyyy-MM");
                results[key] = 0;
            }

            // Calculate days off per month
            foreach (var request in approvedRequests)
            {
                var days = (request.EndDate.DayNumber - request.StartDate.DayNumber) + 1;
                var key = request.StartDate.ToString("yyyy-MM");

                if (results.ContainsKey(key))
                {
                    results[key] += days;
                }
            }

            return results;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error calculating time-off by month");
            return new Dictionary<string, int>();
        }
    }
}
