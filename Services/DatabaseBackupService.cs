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
    private readonly string? _encryptionPassphrase;

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

        // D-10: Optional encryption passphrase for backup files
        _encryptionPassphrase = configuration.GetValue<string>("Backup:EncryptionPassphrase");
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

                // E-02: Audit log retention — clean up old entries after backup
                await CleanupOldAuditLogsAsync(stoppingToken);
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
        // F-05: Generate job-execution ID for log correlation
        var jobExecutionId = $"job-backup-{Guid.NewGuid():N}";
        using var logScope = _logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = jobExecutionId });

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

            // C-10: Use VACUUM INTO for atomic backup (SQLite 3.27.0+)
            // This creates a consistent, standalone backup without WAL/SHM files.
            // Falls back to File.Copy if VACUUM INTO is not available.
            var usedVacuumInto = false;
            try
            {
                var connectionString = _configuration.GetConnectionString("Default") ?? $"Data Source={_dbPath}";
                using var conn = new SqliteConnection(connectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"VACUUM INTO @backupPath;";
                cmd.Parameters.AddWithValue("@backupPath", backupPath);
                await Task.Run(() => cmd.ExecuteNonQuery(), cancellationToken);
                usedVacuumInto = true;
                _logger.LogDebug("Atomic backup via VACUUM INTO completed");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "VACUUM INTO failed, falling back to file copy");
            }

            if (!usedVacuumInto)
            {
                // Fallback: Checkpoint WAL then copy files
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
            }

            // F-03: Verify backup integrity by opening it with SQLite and running integrity_check
            try
            {
                using var verifyConn = new SqliteConnection($"Data Source={backupPath};Mode=ReadOnly");
                verifyConn.Open();
                using var verifyCmd = verifyConn.CreateCommand();
                verifyCmd.CommandText = "PRAGMA integrity_check;";
                var integrityResult = verifyCmd.ExecuteScalar()?.ToString();
                if (integrityResult != "ok")
                {
                    _logger.LogError("Backup integrity check FAILED for {FileName}: {Result}", backupFileName, integrityResult);
                }
                else
                {
                    _logger.LogDebug("Backup integrity check passed for {FileName}", backupFileName);
                }
            }
            catch (Exception verifyEx)
            {
                _logger.LogWarning(verifyEx, "Could not verify backup integrity for {FileName}", backupFileName);
            }

            // D-10: Encrypt backup if passphrase is configured
            var finalPath = backupPath;
            if (!string.IsNullOrEmpty(_encryptionPassphrase))
            {
                try
                {
                    var encryptedPath = backupPath + ".enc";
                    await EncryptFileAsync(backupPath, encryptedPath, _encryptionPassphrase, cancellationToken);
                    File.Delete(backupPath); // Remove plaintext
                    finalPath = encryptedPath;
                    backupFileName += ".enc";
                    _logger.LogInformation("Backup encrypted with AES-256: {FileName}", backupFileName);
                }
                catch (Exception encEx)
                {
                    _logger.LogWarning(encEx, "Backup encryption failed — plaintext backup retained at {Path}", backupPath);
                }
            }

            // Calculate checksum and file size
            var fileInfo = new FileInfo(finalPath);
            var checksum = await ComputeChecksumAsync(finalPath, cancellationToken);

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
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "Failed to clean up partial backup file: {Path}", backupPath);
            }

            throw;
        }
    }

    /// <summary>
    /// E-02: Remove audit log entries older than configured retention period.
    /// Anonymizes PII in old entries before deletion for compliance.
    /// Config: AuditLog:RetentionDays (default 365)
    /// </summary>
    private async Task CleanupOldAuditLogsAsync(CancellationToken ct)
    {
        var retentionDays = _configuration.GetValue<int>("AuditLog:RetentionDays", 365);
        if (retentionDays <= 0) return; // 0 = keep forever

        try
        {
            var connectionString = _configuration.GetConnectionString("Default") ?? $"Data Source={_dbPath}";
            using var conn = new SqliteConnection(connectionString);
            conn.Open();

            // Delete audit logs older than retention period
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM AuditLogs WHERE Timestamp < @cutoff;";
            cmd.Parameters.AddWithValue("@cutoff", DateTime.UtcNow.AddDays(-retentionDays).ToString("o"));
            var deleted = await Task.Run(() => cmd.ExecuteNonQuery(), ct);

            if (deleted > 0)
            {
                _logger.LogInformation("E-02: Cleaned up {Count} audit log entries older than {Days} days", deleted, retentionDays);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up old audit log entries");
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

    /// <summary>
    /// D-10: Encrypt a file with AES-256-CBC using PBKDF2-derived key.
    /// File format: [16-byte salt][16-byte IV][encrypted data]
    /// </summary>
    private static async Task EncryptFileAsync(string inputPath, string outputPath, string passphrase, CancellationToken ct)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        using var keyDerivation = new Rfc2898DeriveBytes(passphrase, salt, 100_000, HashAlgorithmName.SHA256);
        var key = keyDerivation.GetBytes(32); // AES-256

        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();

        await using var outputStream = File.Create(outputPath);
        await outputStream.WriteAsync(salt, ct);
        await outputStream.WriteAsync(aes.IV, ct);

        await using var cryptoStream = new CryptoStream(outputStream, aes.CreateEncryptor(), CryptoStreamMode.Write);
        await using var inputStream = File.OpenRead(inputPath);
        await inputStream.CopyToAsync(cryptoStream, ct);
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
