using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Services;

namespace ShiftManager.Pages.Owner;

/// <summary>
/// Owner Administration Panel - Main Dashboard
/// Provides overview and quick access to developer/owner tools
/// </summary>
[Authorize(Policy = "IsAdmin")]
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        AppDbContext db,
        IAuditLogService auditLogService,
        ILogger<IndexModel> logger)
    {
        _db = db;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    // Quick Stats
    public int TotalUsers { get; set; }
    public int TotalCompanies { get; set; }
    public string DatabaseSize { get; set; } = "0 MB";
    public string Uptime { get; set; } = "0d 0h 0m";

    // Recent Activity
    public List<ActivityItem> RecentActivities { get; set; } = new();

    public async Task OnGetAsync()
    {
        try
        {
            // Calculate stats
            TotalUsers = await _db.Users.IgnoreQueryFilters().CountAsync();
            TotalCompanies = await _db.Companies.CountAsync();
            DatabaseSize = GetDatabaseSize();
            Uptime = GetUptime();

            // Load recent audit activities
            var recentLogs = await _auditLogService.GetRecentLogsAsync(10);
            RecentActivities = recentLogs.Select(log => new ActivityItem
            {
                Icon = GetActivityIcon(log.Action),
                Description = $"{log.Action}: {log.Description}",
                Timestamp = log.Timestamp
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Owner dashboard");
        }
    }

    private string GetDatabaseSize()
    {
        try
        {
            var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "app.db");
            if (System.IO.File.Exists(dbPath))
            {
                var fileInfo = new System.IO.FileInfo(dbPath);
                var sizeInMB = fileInfo.Length / 1024.0 / 1024.0;
                return $"{sizeInMB:F2} MB";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating database size");
        }
        return "Unknown";
    }

    private string GetUptime()
    {
        try
        {
            var uptime = DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime;
            if (uptime.TotalDays >= 1)
                return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
            else if (uptime.TotalHours >= 1)
                return $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
            else
                return $"{uptime.Minutes}m {uptime.Seconds}s";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating uptime");
            return "Unknown";
        }
    }

    private string GetActivityIcon(string action)
    {
        return action.ToLower() switch
        {
            var a when a.Contains("create") => "➕",
            var a when a.Contains("update") || a.Contains("edit") => "✏️",
            var a when a.Contains("delete") || a.Contains("cancel") => "🗑️",
            var a when a.Contains("login") => "🔐",
            var a when a.Contains("approve") => "✅",
            var a when a.Contains("reject") || a.Contains("decline") => "❌",
            var a when a.Contains("assign") => "👤",
            _ => "📝"
        };
    }

    public class ActivityItem
    {
        public string Icon { get; set; } = "";
        public string Description { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }
}
