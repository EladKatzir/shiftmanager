using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ShiftManager.Data;
using System.Diagnostics;

namespace ShiftManager.Pages.Owner;

/// <summary>
/// System Health Dashboard - Monitor application health and performance
/// </summary>
[Authorize(Policy = "IsAdmin")]
public class SystemHealthModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<SystemHealthModel> _logger;

    public SystemHealthModel(
        AppDbContext db,
        IConfiguration configuration,
        IWebHostEnvironment env,
        ILogger<SystemHealthModel> logger)
    {
        _db = db;
        _configuration = configuration;
        _env = env;
        _logger = logger;
    }

    // Overall Status
    public string OverallStatus { get; set; } = "Healthy";

    // Database Health
    public bool DatabaseHealthy { get; set; }
    public string DatabaseSize { get; set; } = "0 MB";
    public int TableCount { get; set; }
    public long TotalRecords { get; set; }

    // Memory Usage
    public bool MemoryHealthy { get; set; }
    public long MemoryUsedMB { get; set; }
    public int GCGen0Collections { get; set; }
    public int GCGen2Collections { get; set; }

    // Uptime
    public DateTime StartTime { get; set; }
    public string Uptime { get; set; } = "";
    public string Environment { get; set; } = "";

    // Configuration
    public bool EmailConfigured { get; set; }
    public bool AdfsConfigured { get; set; }
    public bool DailyNotificationsEnabled { get; set; }
    public int CompanyCount { get; set; }
    public int UserCount { get; set; }

    // Logs
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public int InfoCount { get; set; }

    public async Task OnGetAsync()
    {
        try
        {
            // Check Database Health
            await CheckDatabaseHealthAsync();

            // Check Memory
            CheckMemoryHealth();

            // Check Uptime
            CheckUptime();

            // Check Configuration
            await CheckConfigurationAsync();

            // Check Logs (placeholder - would need log file parsing)
            CheckLogs();

            // Determine overall status
            OverallStatus = DetermineOverallStatus();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading system health dashboard");
            OverallStatus = "Error";
        }
    }

    private async Task CheckDatabaseHealthAsync()
    {
        try
        {
            // Test database connection
            DatabaseHealthy = await _db.Database.CanConnectAsync();

            // Get database size
            var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "app.db");
            if (System.IO.File.Exists(dbPath))
            {
                var fileInfo = new System.IO.FileInfo(dbPath);
                var sizeInMB = fileInfo.Length / 1024.0 / 1024.0;
                DatabaseSize = $"{sizeInMB:F2} MB";
            }

            // Count tables (approximate)
            TableCount = 25; // Hardcoded estimate for performance

            // Count total records
            TotalRecords = await _db.Users.IgnoreQueryFilters().CountAsync() +
                          await _db.Companies.CountAsync() +
                          await _db.ShiftInstances.CountAsync() +
                          await _db.Chores.CountAsync() +
                          await _db.OnDuties.IgnoreQueryFilters().CountAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking database health");
            DatabaseHealthy = false;
        }
    }

    private void CheckMemoryHealth()
    {
        try
        {
            var currentProcess = Process.GetCurrentProcess();
            MemoryUsedMB = currentProcess.WorkingSet64 / 1024 / 1024;

            // Get GC statistics
            GCGen0Collections = GC.CollectionCount(0);
            GCGen2Collections = GC.CollectionCount(2);

            // Memory is healthy if under 500MB
            MemoryHealthy = MemoryUsedMB < 500;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking memory health");
            MemoryHealthy = false;
        }
    }

    private void CheckUptime()
    {
        try
        {
            var process = Process.GetCurrentProcess();
            StartTime = process.StartTime;

            var uptime = DateTime.Now - StartTime;
            if (uptime.TotalDays >= 1)
                Uptime = $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
            else if (uptime.TotalHours >= 1)
                Uptime = $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
            else
                Uptime = $"{uptime.Minutes}m {uptime.Seconds}s";

            Environment = _env.EnvironmentName;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking uptime");
            Uptime = "Unknown";
        }
    }

    private async Task CheckConfigurationAsync()
    {
        try
        {
            // Check email configuration
            var emailEnabled = _configuration.GetValue<bool>("Email:Enabled", false);
            var emailApiKey = _configuration.GetValue<string>("Email:ApiKey", "");
            EmailConfigured = emailEnabled && !string.IsNullOrEmpty(emailApiKey);

            // Check ADFS/Griffin configuration
            var griffinConfig = await _db.GriffinConfigs.FirstOrDefaultAsync();
            AdfsConfigured = griffinConfig != null &&
                            griffinConfig.Enabled &&
                            !string.IsNullOrEmpty(griffinConfig.BaseUrl) &&
                            !string.IsNullOrEmpty(griffinConfig.TokenConsumerUrl);

            // Check daily notifications feature flag
            DailyNotificationsEnabled = _configuration.GetValue<bool>("Features:EnableDailyNotifications", true);

            // Get counts
            CompanyCount = await _db.Companies.CountAsync();
            UserCount = await _db.Users.IgnoreQueryFilters().CountAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking configuration");
        }
    }

    private void CheckLogs()
    {
        // Placeholder: In production, you would parse log files or query a logging database
        // For now, return simulated values
        ErrorCount = 0;
        WarningCount = 2;
        InfoCount = 150;

        // You could implement actual log file parsing here
        // For example, read from Logs/log-{date}.txt and count log levels
    }

    private string DetermineOverallStatus()
    {
        if (!DatabaseHealthy)
            return "Critical";

        if (ErrorCount > 10 || !MemoryHealthy)
            return "Warning";

        return "Healthy";
    }
}
