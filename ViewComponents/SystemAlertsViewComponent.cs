using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;

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
    private readonly IGrantService _grantService;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly ILogger<SystemAlertsViewComponent> _logger;
    private const string CacheKey = "SystemAlerts";

    public SystemAlertsViewComponent(IMemoryCache cache, IConfiguration configuration, IWebHostEnvironment env, IGrantService grantService, IStringLocalizer<SharedResources> localizer, ILogger<SystemAlertsViewComponent> logger)
    {
        _cache = cache;
        _configuration = configuration;
        _env = env;
        _grantService = grantService;
        _localizer = localizer;
        _logger = logger;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        // Only show for users with ViewSystemAlerts grant
        var userIdClaim = HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId) || !await _grantService.HasGrantAsync(userId, "ViewSystemAlerts"))
        {
            return Content(string.Empty);
        }

        var cultureCacheKey = $"{CacheKey}_{System.Globalization.CultureInfo.CurrentUICulture.Name}";
        var alerts = (_cache.GetOrCreate(cultureCacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);
            return CollectAlerts();
        }) ?? new List<string>()).ToList();

        var isOwner = HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value == "Owner";
        if (isOwner)
        {
            var hmacSecret = _configuration.GetValue<string>("Security:ApiKeyHmacSecret");
            if (!_env.IsDevelopment() && (string.IsNullOrEmpty(hmacSecret) || hmacSecret == "ShiftManager-ApiKey-HMAC-v1-Default"))
                alerts.Add(_localizer["SystemAlert_DefaultHmacSecret"]);
        }

        return View(alerts);
    }

    private List<string> CollectAlerts()
    {
        var alerts = new List<string>();

        // Parse DB path from connection string (not hardcoded)
        var connStr = _configuration.GetConnectionString("Default") ?? "Data Source=app.db";
        var dbPath = Path.GetFullPath(ShiftManager.Services.DatabaseBackupService.ExtractDbPath(connStr));

        // C-07: Check WAL file size (large WAL = checkpoint issues)
        try
        {
            var walPath = dbPath + "-wal";
            if (File.Exists(walPath))
            {
                var walSizeMb = new FileInfo(walPath).Length / (1024.0 * 1024.0);
                if (walSizeMb > 100)
                    alerts.Add(_localizer["SystemAlert_WalSize", walSizeMb.ToString("F0")]);
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "SystemAlerts: Failed to check WAL size"); }

        // Check disk space
        try
        {
            var driveInfo = new DriveInfo(Path.GetPathRoot(dbPath) ?? "C");
            var freePercent = (double)driveInfo.AvailableFreeSpace / driveInfo.TotalSize * 100;
            if (freePercent < 5)
                alerts.Add(_localizer["SystemAlert_DiskCritical", freePercent.ToString("F1")]);
            else if (freePercent < 10)
                alerts.Add(_localizer["SystemAlert_DiskLow", freePercent.ToString("F1")]);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "SystemAlerts: Failed to check disk space"); }

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
                    alerts.Add(_localizer["SystemAlert_NoBackups"]);
                else if ((DateTime.Now - latestBackup.CreationTime).TotalDays > 2)
                    alerts.Add(_localizer["SystemAlert_BackupOld", ((DateTime.Now - latestBackup.CreationTime).TotalDays).ToString("F0")]);
            }
            else
            {
                alerts.Add(_localizer["SystemAlert_NoBackupDir"]);
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "SystemAlerts: Failed to check backup status"); }

        // Check DataProtection keys
        try
        {
            var keysDir = Path.Combine(AppContext.BaseDirectory, "DataProtection-Keys");
            if (!Directory.Exists(keysDir) || !Directory.GetFiles(keysDir, "*.xml").Any())
                alerts.Add(_localizer["SystemAlert_NoDataProtectionKeys"]);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "SystemAlerts: Failed to check data protection"); }

        // G-04: Check Hebrew font availability for PDF exports
        try
        {
            var fontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            if (!string.IsNullOrEmpty(fontsDir) && Directory.Exists(fontsDir))
            {
                var hasArial = Directory.GetFiles(fontsDir, "arial*.ttf").Length > 0;
                if (!hasArial)
                    alerts.Add(_localizer["SystemAlert_NoHebrewFont"]);
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "SystemAlerts: Failed to check font availability"); }

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
                    alerts.Add(_localizer["SystemAlert_NotificationFailures", errors.ToString(), lastRun.ToString("g")]);
                if ((DateTime.UtcNow - lastRun).TotalHours > 24)
                    alerts.Add(_localizer["SystemAlert_NoNotificationRun", ((DateTime.UtcNow - lastRun).TotalHours).ToString("F0")]);
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "SystemAlerts: Failed to check notification stats"); }

        return alerts;
    }
}
