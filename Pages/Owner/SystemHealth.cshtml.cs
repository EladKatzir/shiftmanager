using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ShiftManager.Data;
using ShiftManager.Data.SeedData;
using ShiftManager.Services;
using System.Diagnostics;

namespace ShiftManager.Pages.Owner;

/// <summary>
/// System Health Dashboard - Monitor application health and performance
/// </summary>
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — Owner page requires Grant:AdminAccess (all 107 grants)
[Authorize(Policy = "Grant:AdminAccess")]
public class SystemHealthModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<SystemHealthModel> _logger;
    private readonly IFeatureFlagService _featureFlagService;

    public SystemHealthModel(
        AppDbContext db,
        IConfiguration configuration,
        IWebHostEnvironment env,
        ILogger<SystemHealthModel> logger,
        IFeatureFlagService featureFlagService)
    {
        _db = db;
        _configuration = configuration;
        _env = env;
        _logger = logger;
        _featureFlagService = featureFlagService;
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
    public List<RecentErrorEntry> RecentErrors { get; set; } = new();

    // Disk
    public long DiskFreeSpaceMB { get; set; }
    public string DiskDrive { get; set; } = "";

    // Security Warnings (fixes D-01, B-06, H-07, B-10)
    public bool HasDefaultCredentials { get; set; }
    public bool PublicSignupEnabled { get; set; }
    public List<string> SecurityWarnings { get; set; } = new();

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

            // Check Logs from audit trail
            await CheckLogsAsync();

            // Check Disk Space
            CheckDiskSpace();

            // Check Security Warnings
            await CheckSecurityWarningsAsync();

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

            // Check daily notifications feature flag (DB-backed)
            DailyNotificationsEnabled = await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.EnableDailyNotifications);

            // Get counts
            CompanyCount = await _db.Companies.CountAsync();
            UserCount = await _db.Users.IgnoreQueryFilters().CountAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking configuration");
        }
    }

    private async Task CheckLogsAsync()
    {
        try
        {
            // Query recent audit log entries for error-like actions
            var recentErrors = await _db.AuditLogs
                .IgnoreQueryFilters()
                .Where(a => a.Action.Contains("Error") || a.Action.Contains("Failed") || a.Action.Contains("Denied"))
                .OrderByDescending(a => a.Timestamp)
                .Take(50)
                .Select(a => new RecentErrorEntry
                {
                    Timestamp = a.Timestamp,
                    Action = a.Action,
                    Description = a.Description ?? "",
                    UserEmail = a.UserEmail ?? "System"
                })
                .ToListAsync();

            RecentErrors = recentErrors;
            ErrorCount = recentErrors.Count;

            // Get counts from audit log for the last 24 hours
            var since = DateTime.UtcNow.AddHours(-24);
            InfoCount = await _db.AuditLogs
                .IgnoreQueryFilters()
                .Where(a => a.Timestamp >= since)
                .CountAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking logs");
            ErrorCount = 0;
            InfoCount = 0;
        }
    }

    private void CheckDiskSpace()
    {
        try
        {
            var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "app.db");
            var root = Path.GetPathRoot(dbPath) ?? "C:\\";
            var driveInfo = new DriveInfo(root);
            DiskFreeSpaceMB = driveInfo.AvailableFreeSpace / 1024 / 1024;
            DiskDrive = driveInfo.Name;
        }
        catch
        {
            DiskFreeSpaceMB = -1;
        }
    }

    private async Task CheckSecurityWarningsAsync()
    {
        // Check for default owner credentials (fixes D-01, B-06)
        var ownerUser = await _db.Users.IgnoreQueryFilters()
            .Include(u => u.RoleTemplate)
            .FirstOrDefaultAsync(u => u.Role == ShiftManager.Models.Support.UserRole.Owner
                || (u.RoleTemplate != null && u.RoleTemplate.DerivedUserRole == ShiftManager.Models.Support.UserRole.Owner));

        if (ownerUser != null)
        {
            if (ShiftManager.Models.PasswordHasher.Verify("admin123", ownerUser.PasswordHash, ownerUser.PasswordSalt))
            {
                HasDefaultCredentials = true;
                SecurityWarnings.Add("Owner account is using the default password 'admin123'. Change immediately.");
            }
        }

        // Check for public signup enabled (fixes H-07, B-10) — reads from DB-backed service
        PublicSignupEnabled = await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.AllowPublicSignup);
        if (PublicSignupEnabled)
        {
            SecurityWarnings.Add("Public signup is enabled. Anyone with server access can create an account.");
        }
    }

    private string DetermineOverallStatus()
    {
        if (!DatabaseHealthy)
            return "Critical";

        if (ErrorCount > 10 || !MemoryHealthy || HasDefaultCredentials)
            return "Warning";

        if (SecurityWarnings.Count > 0)
            return "Warning";

        if (DiskFreeSpaceMB >= 0 && DiskFreeSpaceMB < 100)
            return "Warning";

        return "Healthy";
    }
}

public class RecentErrorEntry
{
    public DateTime Timestamp { get; set; }
    public string Action { get; set; } = "";
    public string Description { get; set; } = "";
    public string UserEmail { get; set; } = "";
}
