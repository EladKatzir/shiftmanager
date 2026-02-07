using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace ShiftManager.Services;

/// <summary>
/// Background service that automatically backs up the SQLite database.
/// - Runs backup on startup
/// - Daily scheduled backups (configurable time)
/// - 7-day rotation (configurable retention)
/// - Logs every backup with file size and SHA256 checksum
/// Fixes: C-02 (no automated backup), E-07 (backup Owner-only dependency)
/// </summary>
public class DatabaseBackupService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DatabaseBackupService> _logger;
    private readonly string _dbPath;
    private readonly string _backupDirectory;
    private readonly int _retentionDays;
    private readonly TimeOnly _scheduledTime;

    public DatabaseBackupService(
        IConfiguration configuration,
        ILogger<DatabaseBackupService> logger)
    {
        _configuration = configuration;
        _logger = logger;

        // Get database path from connection string
        var connectionString = configuration.GetConnectionString("Default") ?? "Data Source=app.db";
        _dbPath = ExtractDbPath(connectionString);

        // Configurable backup directory, defaults to ./Backups
        _backupDirectory = configuration.GetValue<string>("Backup:Directory") ?? "Backups";

        // Configurable retention period, defaults to 7 days
        _retentionDays = configuration.GetValue<int>("Backup:RetentionDays", 7);

        // Configurable backup time, defaults to 03:00 local time
        var timeStr = configuration.GetValue<string>("Backup:ScheduledTime") ?? "03:00";
        _scheduledTime = TimeOnly.Parse(timeStr);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Database Backup Service started. Schedule: daily at {Time}, retention: {Days} days, directory: {Dir}",
            _scheduledTime, _retentionDays, Path.GetFullPath(_backupDirectory));

        // Wait briefly for app to fully initialize
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        // Backup on startup
        try
        {
            await PerformBackupAsync("startup", stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform startup backup");
        }

        // Daily backup loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.Now;
                var scheduledToday = new DateTime(now.Year, now.Month, now.Day,
                    _scheduledTime.Hour, _scheduledTime.Minute, 0);

                // If scheduled time already passed today, schedule for tomorrow
                if (scheduledToday <= now)
                {
                    scheduledToday = scheduledToday.AddDays(1);
                }

                var delay = scheduledToday - now;
                _logger.LogDebug("Next scheduled backup at {Time} (in {Hours:F1} hours)",
                    scheduledToday, delay.TotalHours);

                await Task.Delay(delay, stoppingToken);

                await PerformBackupAsync("scheduled", stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in backup scheduling loop");
                // Wait 1 hour before retrying on error
                try { await Task.Delay(TimeSpan.FromHours(1), stoppingToken); }
                catch (TaskCanceledException) { break; }
            }
        }

        _logger.LogInformation("Database Backup Service stopped");
    }

    /// <summary>
    /// Performs a backup of the SQLite database file.
    /// Can be called externally for manual/on-demand backups.
    /// </summary>
    public async Task<string?> PerformBackupAsync(string reason, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_dbPath))
        {
            _logger.LogWarning("Database file not found at {Path}, skipping backup", _dbPath);
            return null;
        }

        // Ensure backup directory exists
        Directory.CreateDirectory(_backupDirectory);

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        // Sanitize reason to prevent path traversal — alphanumeric + hyphens only
        var safeReason = new string(reason.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
        if (string.IsNullOrEmpty(safeReason)) safeReason = "manual";
        var backupFileName = $"app.db.backup-{timestamp}-{safeReason}";
        var backupPath = Path.Combine(_backupDirectory, backupFileName);

        try
        {
            _logger.LogInformation("Starting database backup: {Reason}", reason);

            // Checkpoint WAL before copying to ensure all data is in the main DB file
            try
            {
                var connectionString = _configuration.GetConnectionString("Default") ?? $"Data Source={_dbPath}";
                using var conn = new SqliteConnection(connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA wal_checkpoint(PASSIVE);";
                cmd.ExecuteNonQuery();
                _logger.LogDebug("WAL checkpoint completed before backup");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WAL checkpoint before backup failed, proceeding with file copy");
            }

            // Copy the database file
            await Task.Run(() => File.Copy(_dbPath, backupPath, overwrite: false), cancellationToken);

            // Also copy WAL and SHM files if they exist (for complete backup)
            var walPath = _dbPath + "-wal";
            var shmPath = _dbPath + "-shm";

            if (File.Exists(walPath))
            {
                await Task.Run(() => File.Copy(walPath, backupPath + "-wal", overwrite: false), cancellationToken);
            }

            if (File.Exists(shmPath))
            {
                await Task.Run(() => File.Copy(shmPath, backupPath + "-shm", overwrite: false), cancellationToken);
            }

            // Calculate checksum and file size
            var fileInfo = new FileInfo(backupPath);
            var checksum = await ComputeChecksumAsync(backupPath, cancellationToken);

            _logger.LogInformation(
                "Database backup completed. File: {FileName}, Size: {SizeKB:F1} KB, SHA256: {Checksum}, Reason: {Reason}",
                backupFileName, fileInfo.Length / 1024.0, checksum, reason);

            // Clean up old backups
            CleanupOldBackups();

            return backupPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to backup database to {Path}", backupPath);

            // Clean up partial backup
            try { if (File.Exists(backupPath)) File.Delete(backupPath); }
            catch { /* ignore cleanup errors */ }

            throw;
        }
    }

    private void CleanupOldBackups()
    {
        try
        {
            if (!Directory.Exists(_backupDirectory))
                return;

            var cutoff = DateTime.Now.AddDays(-_retentionDays);
            var backupFiles = Directory.GetFiles(_backupDirectory, "app.db.backup-*")
                .Select(f => new FileInfo(f))
                .Where(f => f.CreationTime < cutoff)
                .OrderBy(f => f.CreationTime)
                .ToList();

            foreach (var file in backupFiles)
            {
                try
                {
                    file.Delete();
                    _logger.LogInformation("Deleted old backup: {FileName} (created {Date})",
                        file.Name, file.CreationTime);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete old backup: {FileName}", file.Name);
                }
            }

            if (backupFiles.Count > 0)
            {
                _logger.LogInformation("Cleaned up {Count} old backup(s) older than {Days} days",
                    backupFiles.Count, _retentionDays);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during backup cleanup");
        }
    }

    private static async Task<string> ComputeChecksumAsync(string filePath, CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ExtractDbPath(string connectionString)
    {
        // Parse "Data Source=app.db" or "Data Source=path/to/app.db"
        var parts = connectionString.Split('=', 2);
        return parts.Length > 1 ? parts[1].Trim() : "app.db";
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Database Backup Service is stopping...");
        await base.StopAsync(cancellationToken);
    }
}
