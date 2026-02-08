using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using ShiftManager.Models.Support;

namespace ShiftManager.ViewComponents;

/// <summary>
/// F-07: Shows critical system alerts as a banner on admin/owner pages.
/// Checks for: disk space, backup failures, notification failures, security warnings.
/// Results are cached for 15 minutes to avoid per-request overhead.
/// </summary>
public class SystemAlertsViewComponent : ViewComponent
{
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;
    private const string CacheKey = "SystemAlerts";

    public SystemAlertsViewComponent(IMemoryCache cache, IConfiguration configuration, IWebHostEnvironment env)
    {
        _cache = cache;
        _configuration = configuration;
        _env = env;
    }

    public IViewComponentResult Invoke()
    {
        // Only show for Owner/Admin roles
        if (!HttpContext.User.IsInRole(nameof(UserRole.Owner)) &&
            !HttpContext.User.IsInRole(nameof(UserRole.Manager)))
        {
            return Content(string.Empty);
        }

        var alerts = _cache.GetOrCreate(CacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);
            return CollectAlerts();
        }) ?? new List<string>();

        return View(alerts);
    }

    private List<string> CollectAlerts()
    {
        var alerts = new List<string>();

        // C-07: Check WAL file size (large WAL = checkpoint issues)
        try
        {
            var walPath = Path.Combine(AppContext.BaseDirectory, "app.db-wal");
            if (File.Exists(walPath))
            {
                var walSizeMb = new FileInfo(walPath).Length / (1024.0 * 1024.0);
                if (walSizeMb > 100)
                    alerts.Add($"WARNING: SQLite WAL file is {walSizeMb:F0}MB. Consider running PRAGMA wal_checkpoint(TRUNCATE) during maintenance.");
            }
        }
        catch { /* Ignore WAL check errors */ }

        // Check disk space
        try
        {
            var dbPath = Path.Combine(AppContext.BaseDirectory, "app.db");
            var driveInfo = new DriveInfo(Path.GetPathRoot(dbPath) ?? "C");
            var freePercent = (double)driveInfo.AvailableFreeSpace / driveInfo.TotalSize * 100;
            if (freePercent < 10)
                alerts.Add($"CRITICAL: Disk space is critically low ({freePercent:F1}% free). Database may fail.");
            else if (freePercent < 20)
                alerts.Add($"WARNING: Disk space is low ({freePercent:F1}% free).");
        }
        catch { /* Ignore disk check errors */ }

        // Check if backups directory exists and has recent backups
        try
        {
            var backupDir = _configuration.GetValue<string>("Backup:Directory") ?? "Backups";
            if (Directory.Exists(backupDir))
            {
                var latestBackup = Directory.GetFiles(backupDir, "app.db.backup-*")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTime)
                    .FirstOrDefault();

                if (latestBackup == null)
                    alerts.Add("WARNING: No database backups found.");
                else if ((DateTime.Now - latestBackup.CreationTime).TotalDays > 2)
                    alerts.Add($"WARNING: Last backup is {(DateTime.Now - latestBackup.CreationTime).TotalDays:F0} days old.");
            }
            else
            {
                alerts.Add("WARNING: Backup directory does not exist.");
            }
        }
        catch { /* Ignore backup check errors */ }

        // Check DataProtection keys
        try
        {
            var keysDir = Path.Combine(AppContext.BaseDirectory, "DataProtection-Keys");
            if (!Directory.Exists(keysDir) || !Directory.GetFiles(keysDir, "*.xml").Any())
                alerts.Add("WARNING: DataProtection keys are missing. Sessions will be invalidated on restart.");
        }
        catch { /* Ignore */ }

        // Check HMAC secret (skip in Development — not relevant for local dev)
        var hmacSecret = _configuration.GetValue<string>("Security:ApiKeyHmacSecret");
        if (!_env.IsDevelopment() && (string.IsNullOrEmpty(hmacSecret) || hmacSecret == "ShiftManager-ApiKey-HMAC-v1-Default"))
            alerts.Add("SECURITY: API key HMAC secret is using default value. Configure Security:ApiKeyHmacSecret.");

        // G-04: Check Hebrew font availability for PDF exports
        try
        {
            var fontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            if (!string.IsNullOrEmpty(fontsDir) && Directory.Exists(fontsDir))
            {
                var hasArial = Directory.GetFiles(fontsDir, "arial*.ttf").Length > 0;
                if (!hasArial)
                    alerts.Add("WARNING: Arial font not found. PDF exports may render Hebrew text incorrectly. Install Hebrew language pack.");
            }
        }
        catch { /* Ignore font check errors */ }

        // F-08: Check notification delivery stats
        try
        {
            var statsPath = Path.Combine(AppContext.BaseDirectory, "notification-stats.json");
            if (File.Exists(statsPath))
            {
                var json = File.ReadAllText(statsPath);
                var stats = System.Text.Json.JsonDocument.Parse(json);
                var root = stats.RootElement;
                var errors = root.GetProperty("Errors").GetInt32();
                var lastRun = root.GetProperty("LastRun").GetDateTime();
                if (errors > 0)
                    alerts.Add($"WARNING: Last notification run had {errors} delivery failures ({lastRun:g} UTC).");
                if ((DateTime.UtcNow - lastRun).TotalHours > 24)
                    alerts.Add($"WARNING: No notification job run in {(DateTime.UtcNow - lastRun).TotalHours:F0} hours.");
            }
        }
        catch { /* Ignore notification stats errors */ }

        return alerts;
    }
}
