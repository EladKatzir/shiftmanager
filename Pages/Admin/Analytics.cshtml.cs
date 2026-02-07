using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models.Analytics;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Text;

namespace ShiftManager.Pages.Admin;

[Authorize(Policy = "IsManagerOrAdmin")]
public class AnalyticsModel : LocalizedPageModel
{
    private readonly IAnalyticsService _analyticsService;
    private readonly ITenantResolver _tenantResolver;
    private readonly AppDbContext _db;
    private readonly ILogger<AnalyticsModel> _logger;

    public AnalyticsModel(
        IStringLocalizer<SharedResources> localizer,
        IAnalyticsService analyticsService,
        ITenantResolver tenantResolver,
        AppDbContext db,
        ILogger<AnalyticsModel> logger)
        : base(localizer)
    {
        _analyticsService = analyticsService;
        _tenantResolver = tenantResolver;
        _db = db;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public int DateRange { get; set; } = 30; // Default: last 30 days

    // Employee Analytics
    public List<EmployeeHoursDto> EmployeeHours { get; set; } = new();
    public List<EmployeeShiftCountDto> UpcomingShifts { get; set; } = new();
    public List<BackToBackShiftDto> BackToBackShifts { get; set; } = new();

    // Team Analytics
    public Dictionary<UserRole, decimal> HoursByRole { get; set; } = new();
    public List<StaffingIssueDto> UnderstaffingReport { get; set; } = new();
    public List<StaffingIssueDto> OverstaffingReport { get; set; } = new();
    public decimal CoverageRate { get; set; }

    // Swap Analytics
    public SwapStatsDto SwapStats { get; set; } = new();
    public List<TopSwapperDto> TopSwappers { get; set; } = new();
    public TimeSpan AverageSwapApprovalTime { get; set; }

    // Time-Off Analytics
    public TimeOffStatsDto TimeOffStats { get; set; } = new();
    public decimal AverageDaysOffPerEmployee { get; set; }
    public Dictionary<string, int> TimeOffByMonth { get; set; } = new();

    // ✅ PHASE 18: Chores Analytics
    public int ChoresCompletedThisWeek { get; set; }
    public int ChoresPendingThisWeek { get; set; }
    public int ChoresOverdue { get; set; }

    // ✅ PHASE 18: On-Duty Analytics
    public int OnDutyAssignmentsThisWeek { get; set; }
    public int DaysWithoutOnDuty { get; set; }
    public decimal OnDutyCoverageRate { get; set; }

    public async Task OnGetAsync()
    {
        try
        {
            var endDate = DateOnly.FromDateTime(DateTime.Today);
            var startDate = endDate.AddDays(-DateRange);

            // Load all analytics data
            EmployeeHours = await _analyticsService.GetEmployeeHoursAsync(startDate, endDate);
            UpcomingShifts = await _analyticsService.GetUpcomingShiftsAsync(7);
            BackToBackShifts = await _analyticsService.GetBackToBackShiftsAsync(DateRange);

            HoursByRole = await _analyticsService.GetHoursByRoleAsync(startDate, endDate);
            UnderstaffingReport = await _analyticsService.GetUnderstaffingReportAsync(startDate, endDate);
            OverstaffingReport = await _analyticsService.GetOverstaffingReportAsync(startDate, endDate);
            CoverageRate = await _analyticsService.GetCoverageRateAsync(startDate, endDate);

            SwapStats = await _analyticsService.GetSwapStatsAsync(startDate, endDate);
            TopSwappers = await _analyticsService.GetTopSwappersAsync(10, DateRange);
            AverageSwapApprovalTime = await _analyticsService.GetAverageSwapApprovalTimeAsync(DateRange);

            TimeOffStats = await _analyticsService.GetTimeOffStatsAsync(startDate, endDate);
            AverageDaysOffPerEmployee = await _analyticsService.GetAverageDaysOffPerEmployeeAsync(DateRange);
            TimeOffByMonth = await _analyticsService.GetTimeOffByMonthAsync(12);

            // ✅ PHASE 18: Load Chores analytics
            var weekStart = DateOnly.FromDateTime(DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek));
            var weekEnd = weekStart.AddDays(6);
            var today = DateOnly.FromDateTime(DateTime.Today);

            // Chores completed this week (past dates that haven't been canceled)
            ChoresCompletedThisWeek = await _db.Chores
                .Where(c => c.Date >= weekStart && c.Date <= weekEnd && c.Date < today && c.CanceledAt == null)
                .CountAsync();

            // Chores pending this week (future/today dates, not canceled)
            ChoresPendingThisWeek = await _db.Chores
                .Where(c => c.Date >= today && c.Date <= weekEnd && c.CanceledAt == null)
                .CountAsync();

            // Chores overdue (before today, not canceled)
            ChoresOverdue = await _db.Chores
                .Where(c => c.Date < today && c.Date < weekStart && c.CanceledAt == null)
                .CountAsync();

            // ✅ PHASE 18: Load On-Duty analytics
            // On-Duty assignments this week
            OnDutyAssignmentsThisWeek = await _db.OnDuties
                .Where(od => od.Date >= weekStart && od.Date <= weekEnd && od.CanceledAt == null)
                .CountAsync();

            // Days without On-Duty this week
            var daysWithOnDuty = await _db.OnDuties
                .Where(od => od.Date >= weekStart && od.Date <= weekEnd && od.CanceledAt == null)
                .Select(od => od.Date)
                .Distinct()
                .CountAsync();
            DaysWithoutOnDuty = 7 - daysWithOnDuty;

            // On-Duty coverage rate (% of days with at least one assignment)
            OnDutyCoverageRate = daysWithOnDuty > 0 ? (daysWithOnDuty / 7.0m) * 100 : 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading analytics data");
            TempData["Error"] = _localizer["Error_LoadingAnalyticsData"].Value;
        }
    }

    public async Task<IActionResult> OnGetExportCsvAsync()
    {
        try
        {
            var endDate = DateOnly.FromDateTime(DateTime.Today);
            var startDate = endDate.AddDays(-DateRange);

            var employeeHours = await _analyticsService.GetEmployeeHoursAsync(startDate, endDate);
            var understaffing = await _analyticsService.GetUnderstaffingReportAsync(startDate, endDate);
            var overstaffing = await _analyticsService.GetOverstaffingReportAsync(startDate, endDate);
            var swapStats = await _analyticsService.GetSwapStatsAsync(startDate, endDate);
            var timeOffStats = await _analyticsService.GetTimeOffStatsAsync(startDate, endDate);
            var coverageRate = await _analyticsService.GetCoverageRateAsync(startDate, endDate);

            var company = await _db.Companies.FindAsync(_tenantResolver.GetCurrentTenantId());

            var csv = new StringBuilder();
            csv.AppendLine($"Analytics Report - {company?.Name ?? "Company"}");
            csv.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            csv.AppendLine($"Date Range: {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");
            csv.AppendLine();

            // Employee Hours
            csv.AppendLine("Employee Hours");
            csv.AppendLine("Employee,Total Hours,Avg Hours/Week,Shift Count");
            foreach (var emp in employeeHours)
            {
                csv.AppendLine($"\"{emp.EmployeeName}\",{emp.TotalHours},{emp.AverageHoursPerWeek},{emp.ShiftCount}");
            }
            csv.AppendLine();

            // Coverage Rate
            csv.AppendLine("Coverage Metrics");
            csv.AppendLine($"Overall Coverage Rate,{coverageRate:F2}%");
            csv.AppendLine();

            // Understaffing Report
            csv.AppendLine("Understaffing Report");
            csv.AppendLine("Date,Shift Type,Assigned,Required,Deficit");
            foreach (var issue in understaffing)
            {
                csv.AppendLine($"{issue.WorkDate:yyyy-MM-dd},\"{issue.ShiftType}\",{issue.AssignedCount},{issue.RequiredCount},{issue.Difference}");
            }
            csv.AppendLine();

            // Overstaffing Report
            csv.AppendLine("Overstaffing Report");
            csv.AppendLine("Date,Shift Type,Assigned,Required,Surplus");
            foreach (var issue in overstaffing)
            {
                csv.AppendLine($"{issue.WorkDate:yyyy-MM-dd},\"{issue.ShiftType}\",{issue.AssignedCount},{issue.RequiredCount},{issue.Difference}");
            }
            csv.AppendLine();

            // Swap Statistics
            csv.AppendLine("Swap Request Statistics");
            csv.AppendLine($"Total Requests,{swapStats.TotalRequests}");
            csv.AppendLine($"Approved,{swapStats.ApprovedCount}");
            csv.AppendLine($"Declined,{swapStats.DeclinedCount}");
            csv.AppendLine($"Pending,{swapStats.PendingCount}");
            csv.AppendLine($"Approval Rate,{swapStats.ApprovalRate:F2}%");
            csv.AppendLine();

            // Time-Off Statistics
            csv.AppendLine("Time-Off Request Statistics");
            csv.AppendLine($"Total Requests,{timeOffStats.TotalRequests}");
            csv.AppendLine($"Approved,{timeOffStats.ApprovedCount}");
            csv.AppendLine($"Declined,{timeOffStats.DeclinedCount}");
            csv.AppendLine($"Pending,{timeOffStats.PendingCount}");
            csv.AppendLine($"Approval Rate,{timeOffStats.ApprovalRate:F2}%");

            var fileName = $"Analytics_Report_{company?.Name.Replace(" ", "_")}_{DateTime.UtcNow:yyyyMMdd}.csv";
            // Add UTF-8 BOM for Hebrew Excel compatibility (fixes G-07)
            var preamble = Encoding.UTF8.GetPreamble();
            var csvBytes = Encoding.UTF8.GetBytes(csv.ToString());
            var bomResult = new byte[preamble.Length + csvBytes.Length];
            preamble.CopyTo(bomResult, 0);
            csvBytes.CopyTo(bomResult, preamble.Length);
            return File(bomResult, "text/csv", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting analytics report");
            TempData["Error"] = _localizer["Error_ExportingReport"].Value;
            return RedirectToPage();
        }
    }
}
