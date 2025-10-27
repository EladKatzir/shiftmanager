using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models.Analytics;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using System.Text;

namespace ShiftManager.Pages.Admin;

[Authorize(Policy = "IsManagerOrAdmin")]
public class AnalyticsModel : PageModel
{
    private readonly IAnalyticsService _analyticsService;
    private readonly ITenantResolver _tenantResolver;
    private readonly AppDbContext _db;
    private readonly ILogger<AnalyticsModel> _logger;

    public AnalyticsModel(
        IAnalyticsService analyticsService,
        ITenantResolver tenantResolver,
        AppDbContext db,
        ILogger<AnalyticsModel> logger)
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading analytics data");
            TempData["Error"] = "Error loading analytics data. Please try again.";
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
            return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting analytics report");
            TempData["Error"] = "Error exporting report. Please try again.";
            return RedirectToPage();
        }
    }
}
