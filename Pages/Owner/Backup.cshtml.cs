using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using ShiftManager.Services;
using System.IO.Compression;
using System.Security.Claims;

namespace ShiftManager.Pages.Owner;

/// <summary>
/// Backup and Restore - Manage database backups.
/// </summary>
[Authorize(Policy = "Grant:AdminAccess")]
public class BackupModel : PageModel
{
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<BackupModel> _logger;
    private readonly string _dbPath;
    private readonly string _backupsFolder;
    private readonly string _connectionString;
    private readonly IDeploymentExportService _exportService;

    public BackupModel(
        IConfiguration configuration,
        IAuditLogService auditLogService,
        ILogger<BackupModel> logger,
        IDeploymentExportService exportService)
    {
        _auditLogService = auditLogService;
        _logger = logger;
        _exportService = exportService;

        // Existing connection string and path setup — DO NOT REMOVE
        _connectionString = configuration.GetConnectionString("Default") ?? "Data Source=app.db";
        _dbPath = DatabaseBackupService.ExtractDbPath(_connectionString);
        _backupsFolder = configuration.GetValue<string>("Backup:Directory") ?? "Backups";
    }

    public List<BackupFileInfo> Backups { get; set; } = new();
    public List<BackupFileInfo> SystemBackups { get; set; } = new();
    public string? Success { get; set; }
    public string? Error { get; set; }
    public ExportManifest? LastExportInfo { get; set; }
    public bool ShowRestoreBanner { get; set; }

    public class BackupFileInfo
    {
        public string FileName { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public DateTime CreatedDate { get; set; }
        public bool IsSystemBackup { get; set; }
        public string SizeFormatted => FormatBytes(SizeBytes);

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }

    public void OnGet()
    {
        LoadBackups();
        LastExportInfo = _exportService.GetLastExportInfo();
        if (DeploymentExportService.RestoreJustCompleted)
        {
            ShowRestoreBanner = true;
            DeploymentExportService.RestoreJustCompleted = false;
        }
    }

    public async Task<IActionResult> OnPostCreateBackupAsync()
    {
        try
        {
            var currentUserId = GetCurrentUserId();
            var backupsFolder = _backupsFolder;
            Directory.CreateDirectory(backupsFolder);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupFileName = $"backup_{timestamp}.db";
            var backupFilePath = Path.Combine(backupsFolder, backupFileName);

            var dbPath = _dbPath;

            if (!System.IO.File.Exists(dbPath))
            {
                Error = "Database file not found.";
                LoadBackups();
                return Page();
            }

            // Use VACUUM INTO for a consistent backup (includes WAL data, no separate WAL/SHM files)
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "VACUUM INTO @backupPath;";
            cmd.Parameters.AddWithValue("@backupPath", Path.GetFullPath(backupFilePath));
            await Task.Run(() => cmd.ExecuteNonQuery());

            // Log the backup
            await _auditLogService.LogUserActionAsync(
                currentUserId,
                "BackupCreated",
                "Database",
                null,
                $"Database backup created: {backupFileName}",
                null);

            Success = $"Backup created successfully: {backupFileName}";
            _logger.LogInformation("Database backup created: {FileName}", backupFileName);

            LoadBackups();
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating database backup");
            Error = "An unexpected error occurred. Please try again.";
            LoadBackups();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostPrepareForUpdateAsync()
    {
        try
        {
            var currentUserId = GetCurrentUserId();
            var userEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "unknown";

            var result = await _exportService.ExportAsync(currentUserId, userEmail);

            if (result.Success)
            {
                Success = $"Deployment export created successfully. Database: {result.Manifest!.Database.SizeBytes / 1024}KB, " +
                          $"Avatars: {result.Manifest.AvatarCount}, Feedback images: {result.Manifest.FeedbackImageCount}, " +
                          $"DataProtection keys: {result.Manifest.DataProtectionKeyCount}. " +
                          $"Export saved to {DeploymentExportService.ExportPath}";
            }
            else
            {
                Error = result.ErrorMessage ?? "Export failed.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating deployment export");
            Error = "An unexpected error occurred during export.";
        }

        LoadBackups();
        LastExportInfo = _exportService.GetLastExportInfo();
        return Page();
    }

    public async Task<IActionResult> OnPostRestoreBackupAsync(string backupFileName)
    {
        try
        {
            var currentUserId = GetCurrentUserId();

            if (string.IsNullOrWhiteSpace(backupFileName))
            {
                Error = "Please specify a backup file.";
                LoadBackups();
                return Page();
            }

            var backupFilePath = GetValidatedBackupPath(backupFileName);
            if (backupFilePath == null || !System.IO.File.Exists(backupFilePath))
            {
                Error = "Backup file not found.";
                LoadBackups();
                return Page();
            }

            var backupsFolder = _backupsFolder;
            var dbPath = _dbPath;

            // Step 1: Checkpoint WAL to flush pending writes into the main DB file
            try
            {
                using var checkpointConn = new SqliteConnection(_connectionString);
                checkpointConn.Open();
                using var checkpointCmd = checkpointConn.CreateCommand();
                checkpointCmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                checkpointCmd.ExecuteNonQuery();
                _logger.LogInformation("WAL checkpoint completed before restore");
            }
            catch (Exception walEx)
            {
                _logger.LogWarning(walEx, "WAL checkpoint before restore failed, proceeding anyway");
            }

            // Step 2: Release ALL pooled SQLite file handles (critical on Windows)
            SqliteConnection.ClearAllPools();

            // Step 3: Create a backup of current database before restoring
            var preRestoreBackup = Path.Combine(backupsFolder, $"pre_restore_{DateTime.Now:yyyyMMdd_HHmmss}.db");
            if (System.IO.File.Exists(dbPath))
            {
                using (var sourceStream = new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
                using (var destinationStream = new FileStream(preRestoreBackup, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                {
                    await sourceStream.CopyToAsync(destinationStream);
                }
            }

            // Step 4: Restore the backup (overwrite main DB file)
            using (var sourceStream = new FileStream(backupFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
            using (var destinationStream = new FileStream(dbPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await sourceStream.CopyToAsync(destinationStream);
            }

            // Step 5: Delete stale WAL/SHM files from the old database
            var walPath = dbPath + "-wal";
            var shmPath = dbPath + "-shm";
            if (System.IO.File.Exists(walPath))
            {
                try { System.IO.File.Delete(walPath); }
                catch (Exception ex) { _logger.LogWarning(ex, "Could not delete WAL file after restore"); }
            }
            if (System.IO.File.Exists(shmPath))
            {
                try { System.IO.File.Delete(shmPath); }
                catch (Exception ex) { _logger.LogWarning(ex, "Could not delete SHM file after restore"); }
            }

            // Log the restore
            await _auditLogService.LogUserActionAsync(
                currentUserId,
                "BackupRestored",
                "Database",
                null,
                $"Database restored from backup: {backupFileName}",
                $"Pre-restore backup saved as: {Path.GetFileName(preRestoreBackup)}");

            Success = $"Database restored from backup: {backupFileName}. IMPORTANT: Restart the application now — recycle the IIS App Pool or restart the service (or stop and restart 'dotnet run' in development).";
            _logger.LogWarning("Database restored from backup: {FileName}. Application restart required.", backupFileName);

            LoadBackups();
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring database backup");
            Error = "An unexpected error occurred. Please try again.";
            LoadBackups();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDeleteBackupAsync(string backupFileName)
    {
        try
        {
            var currentUserId = GetCurrentUserId();

            if (string.IsNullOrWhiteSpace(backupFileName))
            {
                Error = "Please specify a backup file.";
                LoadBackups();
                return Page();
            }

            var backupFilePath = GetValidatedBackupPath(backupFileName);
            if (backupFilePath == null || !System.IO.File.Exists(backupFilePath))
            {
                Error = "Backup file not found.";
                LoadBackups();
                return Page();
            }

            System.IO.File.Delete(backupFilePath);

            // Log the deletion
            await _auditLogService.LogUserActionAsync(
                currentUserId,
                "BackupDeleted",
                "Database",
                null,
                $"Backup deleted: {backupFileName}",
                null);

            Success = $"Backup deleted: {backupFileName}";
            _logger.LogInformation("Backup deleted: {FileName}", backupFileName);

            LoadBackups();
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting backup");
            Error = "An unexpected error occurred. Please try again.";
            LoadBackups();
            return Page();
        }
    }

    public async Task<IActionResult> OnGetDownloadBackupAsync(string backupFileName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(backupFileName))
            {
                return NotFound();
            }

            var backupFilePath = GetValidatedBackupPath(backupFileName);
            if (backupFilePath == null || !System.IO.File.Exists(backupFilePath))
            {
                return NotFound();
            }

            var currentUserId = GetCurrentUserId();
            await _auditLogService.LogUserActionAsync(
                currentUserId,
                "BackupDownloaded",
                "Database",
                null,
                $"Backup downloaded: {backupFileName}",
                null);

            return PhysicalFile(backupFilePath, "application/octet-stream", backupFileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading backup");
            return NotFound();
        }
    }

    private void LoadBackups()
    {
        try
        {
            var backupsFolder = _backupsFolder;
            if (!Directory.Exists(backupsFolder))
            {
                Directory.CreateDirectory(backupsFolder);
            }

            // Manual backups (backup_*.db and pre_restore_*.db)
            var backupFiles = Directory.GetFiles(backupsFolder, "*.db");
            Backups = backupFiles
                .Select(f => new BackupFileInfo
                {
                    FileName = Path.GetFileName(f),
                    SizeBytes = new FileInfo(f).Length,
                    CreatedDate = System.IO.File.GetCreationTime(f)
                })
                .OrderByDescending(b => b.CreatedDate)
                .ToList();

            // System backups (auto-backup and pre-migration files created by DatabaseBackupService / Program.cs)
            var systemFiles = new List<string>();
            foreach (var pattern in new[] { "app.db.backup-*", "app.db.pre-migration-*" })
            {
                systemFiles.AddRange(Directory.GetFiles(backupsFolder, pattern));
            }

            SystemBackups = systemFiles
                .Distinct()
                .Select(f => new BackupFileInfo
                {
                    FileName = Path.GetFileName(f),
                    SizeBytes = new FileInfo(f).Length,
                    CreatedDate = System.IO.File.GetCreationTime(f),
                    IsSystemBackup = true
                })
                .OrderByDescending(b => b.CreatedDate)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading backups");
        }
    }

    /// <summary>
    /// Validates backup filename to prevent path traversal attacks.
    /// Returns the safe full path or null if validation fails.
    /// </summary>
    private string? GetValidatedBackupPath(string? backupFileName)
    {
        if (string.IsNullOrWhiteSpace(backupFileName))
            return null;

        // Block path traversal characters
        if (backupFileName.Contains("..") || backupFileName.Contains('/') || backupFileName.Contains('\\'))
        {
            _logger.LogWarning("Path traversal attempt in backup filename: {FileName}", backupFileName);
            return null;
        }

        var backupsFolder = _backupsFolder;
        var fullPath = Path.GetFullPath(Path.Combine(backupsFolder, backupFileName));

        // Ensure resolved path stays within Backups folder
        if (!fullPath.StartsWith(Path.GetFullPath(backupsFolder), StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Path traversal attempt resolved outside Backups folder: {FileName}", backupFileName);
            return null;
        }

        return fullPath;
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : 0;
    }
}
