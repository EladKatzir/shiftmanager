using Microsoft.Extensions.Logging;

namespace ShiftManager.Services;

// Source-generated LoggerMessage delegates for DatabaseBackupService (Batch K Phase 15 — closes 26 sites of CA1848).
// EventId range 18000-18099 reserved for DatabaseBackupService. Range allocations:
//   18000-18009: ExecuteAsync lifecycle / scheduling (5 used)
//   18010-18019: PerformBackupAsync flow (10 used)
//   18020-18029: Encryption + completion + cleanup-on-failure (5 used)
//   18030-18039: CleanupOldAuditLogsAsync (2 used)
//   18040-18049: CleanupOldBackups (4 used)
public partial class DatabaseBackupService
{
    // ── ExecuteAsync lifecycle / scheduling ────────────────────────────────

    [LoggerMessage(EventId = 18000, Level = LogLevel.Information,
        Message = "Database Backup Service started. Schedule: daily at {Time}, retention: {Retention}, directory: {Dir}")]
    private static partial void LogServiceStarted(ILogger logger, System.TimeOnly time, string retention, string dir);

    [LoggerMessage(EventId = 18001, Level = LogLevel.Error,
        Message = "Failed to perform startup backup")]
    private static partial void LogStartupBackupFailed(ILogger logger, System.Exception ex);

    [LoggerMessage(EventId = 18002, Level = LogLevel.Debug,
        Message = "Next scheduled backup at {Time} (in {Hours:F1} hours)")]
    private static partial void LogNextScheduledBackup(ILogger logger, System.DateTime time, double hours);

    [LoggerMessage(EventId = 18003, Level = LogLevel.Error,
        Message = "Error in backup scheduling loop")]
    private static partial void LogSchedulingLoopError(ILogger logger, System.Exception ex);

    [LoggerMessage(EventId = 18004, Level = LogLevel.Information,
        Message = "Database Backup Service stopped")]
    private static partial void LogServiceStopped(ILogger logger);

    // ── PerformBackupAsync flow ────────────────────────────────────────────

    [LoggerMessage(EventId = 18010, Level = LogLevel.Warning,
        Message = "Database file not found at {Path}, skipping backup")]
    private static partial void LogDbFileNotFound(ILogger logger, string path);

    [LoggerMessage(EventId = 18011, Level = LogLevel.Information,
        Message = "Starting database backup: {Reason}")]
    private static partial void LogBackupStarting(ILogger logger, string reason);

    [LoggerMessage(EventId = 18012, Level = LogLevel.Debug,
        Message = "Atomic backup via VACUUM INTO completed")]
    private static partial void LogVacuumIntoCompleted(ILogger logger);

    [LoggerMessage(EventId = 18013, Level = LogLevel.Warning,
        Message = "VACUUM INTO failed, falling back to file copy")]
    private static partial void LogVacuumIntoFailed(ILogger logger, System.Exception ex);

    [LoggerMessage(EventId = 18014, Level = LogLevel.Debug,
        Message = "WAL checkpoint completed before backup")]
    private static partial void LogWalCheckpointCompleted(ILogger logger);

    [LoggerMessage(EventId = 18015, Level = LogLevel.Warning,
        Message = "WAL checkpoint before backup failed, proceeding with file copy")]
    private static partial void LogWalCheckpointFailed(ILogger logger, System.Exception ex);

    [LoggerMessage(EventId = 18016, Level = LogLevel.Error,
        Message = "Backup integrity check FAILED for {FileName}: {Result}")]
    private static partial void LogIntegrityCheckFailed(ILogger logger, string fileName, string? result);

    [LoggerMessage(EventId = 18017, Level = LogLevel.Debug,
        Message = "Backup integrity check passed for {FileName}")]
    private static partial void LogIntegrityCheckPassed(ILogger logger, string fileName);

    [LoggerMessage(EventId = 18018, Level = LogLevel.Warning,
        Message = "Could not verify backup integrity for {FileName}")]
    private static partial void LogIntegrityCheckError(ILogger logger, System.Exception ex, string fileName);

    // ── Encryption + completion + cleanup-on-failure ───────────────────────

    [LoggerMessage(EventId = 18020, Level = LogLevel.Information,
        Message = "Backup encrypted with AES-256: {FileName}")]
    private static partial void LogBackupEncrypted(ILogger logger, string fileName);

    [LoggerMessage(EventId = 18021, Level = LogLevel.Warning,
        Message = "Backup encryption failed — plaintext backup retained at {Path}")]
    private static partial void LogBackupEncryptionFailed(ILogger logger, System.Exception ex, string path);

    [LoggerMessage(EventId = 18022, Level = LogLevel.Information,
        Message = "Database backup completed. File: {FileName}, Size: {SizeKB:F1} KB, SHA256: {Checksum}, Reason: {Reason}")]
    private static partial void LogBackupCompleted(ILogger logger, string fileName, double sizeKB, string checksum, string reason);

    [LoggerMessage(EventId = 18023, Level = LogLevel.Error,
        Message = "Failed to backup database to {Path}")]
    private static partial void LogBackupFailed(ILogger logger, System.Exception ex, string path);

    [LoggerMessage(EventId = 18024, Level = LogLevel.Warning,
        Message = "Failed to clean up partial backup file: {Path}")]
    private static partial void LogPartialBackupCleanupFailed(ILogger logger, System.Exception ex, string path);

    // ── CleanupOldAuditLogsAsync ───────────────────────────────────────────

    [LoggerMessage(EventId = 18030, Level = LogLevel.Information,
        Message = "E-02: Cleaned up {Count} audit log entries older than {Days} days")]
    private static partial void LogAuditLogsCleanedUp(ILogger logger, int count, int days);

    [LoggerMessage(EventId = 18031, Level = LogLevel.Warning,
        Message = "Failed to clean up old audit log entries")]
    private static partial void LogAuditLogCleanupFailed(ILogger logger, System.Exception ex);

    // ── CleanupOldBackups ──────────────────────────────────────────────────

    [LoggerMessage(EventId = 18040, Level = LogLevel.Information,
        Message = "Deleted old backup: {FileName} (created {Date})")]
    private static partial void LogOldBackupDeleted(ILogger logger, string fileName, System.DateTime date);

    [LoggerMessage(EventId = 18041, Level = LogLevel.Warning,
        Message = "Failed to delete old backup: {FileName}")]
    private static partial void LogOldBackupDeleteFailed(ILogger logger, System.Exception ex, string fileName);

    [LoggerMessage(EventId = 18042, Level = LogLevel.Information,
        Message = "Cleaned up {Count} old backup(s), retained max {Max}")]
    private static partial void LogOldBackupsCleanedUp(ILogger logger, int count, int max);

    [LoggerMessage(EventId = 18043, Level = LogLevel.Warning,
        Message = "Error during backup cleanup")]
    private static partial void LogBackupCleanupError(ILogger logger, System.Exception ex);

    // ── StopAsync ──────────────────────────────────────────────────────────

    [LoggerMessage(EventId = 18050, Level = LogLevel.Information,
        Message = "Database Backup Service is stopping...")]
    private static partial void LogServiceStopping(ILogger logger);
}
