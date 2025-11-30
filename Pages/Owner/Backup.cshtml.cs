using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Services;
using System.IO.Compression;
using System.Security.Claims;

namespace ShiftManager.Pages.Owner;

/// <summary>
/// Backup & Restore - Manage database backups
/// </summary>
[Authorize(Policy = "IsAdmin")]
public class BackupModel : PageModel
{
    private readonly IWebHostEnvironment _env;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<BackupModel> _logger;

    public BackupModel(
        IWebHostEnvironment env,
        IAuditLogService auditLogService,
        ILogger<BackupModel> logger)
    {
        _env = env;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public List<BackupFileInfo> Backups { get; set; } = new();
    public string? Success { get; set; }
    public string? Error { get; set; }

    public class BackupFileInfo
    {
        public string FileName { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public DateTime CreatedDate { get; set; }
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
    }

    public async Task<IActionResult> OnPostCreateBackupAsync()
    {
        try
        {
            var currentUserId = GetCurrentUserId();
            var backupsFolder = Path.Combine(_env.ContentRootPath, "Backups");
            Directory.CreateDirectory(backupsFolder);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupFileName = $"backup_{timestamp}.db";
            var backupFilePath = Path.Combine(backupsFolder, backupFileName);

            var dbPath = Path.Combine(_env.ContentRootPath, "app.db");

            if (!System.IO.File.Exists(dbPath))
            {
                Error = "Database file not found.";
                LoadBackups();
                return Page();
            }

            // Copy the database file
            System.IO.File.Copy(dbPath, backupFilePath, overwrite: false);

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
            Error = $"Error creating backup: {ex.Message}";
            LoadBackups();
            return Page();
        }
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

            var backupsFolder = Path.Combine(_env.ContentRootPath, "Backups");
            var backupFilePath = Path.Combine(backupsFolder, backupFileName);

            if (!System.IO.File.Exists(backupFilePath))
            {
                Error = "Backup file not found.";
                LoadBackups();
                return Page();
            }

            var dbPath = Path.Combine(_env.ContentRootPath, "app.db");

            // Create a backup of current database before restoring
            var preRestoreBackup = Path.Combine(backupsFolder, $"pre_restore_{DateTime.Now:yyyyMMdd_HHmmss}.db");
            if (System.IO.File.Exists(dbPath))
            {
                System.IO.File.Copy(dbPath, preRestoreBackup, overwrite: false);
            }

            // Restore the backup
            System.IO.File.Copy(backupFilePath, dbPath, overwrite: true);

            // Log the restore
            await _auditLogService.LogUserActionAsync(
                currentUserId,
                "BackupRestored",
                "Database",
                null,
                $"Database restored from backup: {backupFileName}",
                $"Pre-restore backup saved as: {Path.GetFileName(preRestoreBackup)}");

            Success = $"Database restored from backup: {backupFileName}. Application restart required.";
            _logger.LogWarning("Database restored from backup: {FileName}. Application restart required.", backupFileName);

            LoadBackups();
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring database backup");
            Error = $"Error restoring backup: {ex.Message}";
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

            var backupsFolder = Path.Combine(_env.ContentRootPath, "Backups");
            var backupFilePath = Path.Combine(backupsFolder, backupFileName);

            if (!System.IO.File.Exists(backupFilePath))
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
            Error = $"Error deleting backup: {ex.Message}";
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

            var backupsFolder = Path.Combine(_env.ContentRootPath, "Backups");
            var backupFilePath = Path.Combine(backupsFolder, backupFileName);

            if (!System.IO.File.Exists(backupFilePath))
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

            var fileBytes = await System.IO.File.ReadAllBytesAsync(backupFilePath);
            return File(fileBytes, "application/octet-stream", backupFileName);
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
            var backupsFolder = Path.Combine(_env.ContentRootPath, "Backups");
            if (!Directory.Exists(backupsFolder))
            {
                Directory.CreateDirectory(backupsFolder);
            }

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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading backups");
        }
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : 0;
    }
}
