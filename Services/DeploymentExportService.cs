using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ShiftManager.Data;

namespace ShiftManager.Services;

public class DeploymentExportService : IDeploymentExportService
{
    public const string ExportPath = @"C:\ShiftManager\DeploymentExport";
    public const int MaxRestoredFoldersRetained = 2;

    private static readonly SemaphoreSlim _exportLock = new(1, 1);

    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DeploymentExportService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    // Static flags for two-phase restore
    public static bool PendingDataRestore { get; set; }
    public static volatile bool RestoreJustCompleted;

    public DeploymentExportService(
        IWebHostEnvironment env,
        IConfiguration configuration,
        ILogger<DeploymentExportService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _env = env;
        _configuration = configuration;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    // ================================================================
    // PHASE 1: Static pre-builder restore (config + DataProtection keys)
    // Called from Program.cs BEFORE WebApplication.CreateBuilder()
    // MUST NEVER THROW — wrapped in top-level try/catch
    // ================================================================
    public static void RestoreConfigAndKeys(string? exportPath = null, string? appBaseDir = null)
    {
        exportPath ??= ExportPath;
        appBaseDir ??= AppContext.BaseDirectory;
        PendingDataRestore = false;
        RestoreJustCompleted = false;

        try
        {
            var manifestPath = Path.Combine(exportPath, "manifest.json");
            if (!File.Exists(manifestPath))
                return;

            ExportManifest? manifest;
            try
            {
                var json = File.ReadAllText(manifestPath);
                manifest = JsonSerializer.Deserialize<ExportManifest>(json);
                if (manifest == null)
                {
                    Console.Error.WriteLine($"[DeploymentRestore] manifest.json parsed as null at {manifestPath}");
                    return;
                }
            }
            catch (JsonException ex)
            {
                Console.Error.WriteLine($"[DeploymentRestore] Failed to parse manifest.json: {ex.Message}");
                return;
            }

            Console.WriteLine($"[DeploymentRestore] Phase 1: Export found (exported {manifest.ExportedAt:u} by {manifest.ExportedBy})");

            var configSource = Path.Combine(exportPath, "appsettings.Production.json");
            if (File.Exists(configSource))
            {
                var configDest = Path.Combine(appBaseDir, "appsettings.Production.json");
                File.Copy(configSource, configDest, overwrite: true);
                Console.WriteLine($"[DeploymentRestore] Restored appsettings.Production.json");
            }

            var dpSourceDir = Path.Combine(exportPath, "DataProtection-Keys");
            if (Directory.Exists(dpSourceDir))
            {
                var dpDestDir = Path.Combine(appBaseDir, "DataProtection-Keys");
                Directory.CreateDirectory(dpDestDir);
                foreach (var keyFile in Directory.GetFiles(dpSourceDir, "*.xml"))
                {
                    var destFile = Path.Combine(dpDestDir, Path.GetFileName(keyFile));
                    File.Copy(keyFile, destFile, overwrite: true);
                }
                Console.WriteLine($"[DeploymentRestore] Restored {Directory.GetFiles(dpSourceDir, "*.xml").Length} DataProtection key(s)");
            }

            if (manifest.DataProtectionMachineScoped &&
                !string.IsNullOrEmpty(manifest.MachineName) &&
                !manifest.MachineName.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine(
                    $"[DeploymentRestore] WARNING: DataProtection keys were exported from machine '{manifest.MachineName}' " +
                    $"but this machine is '{Environment.MachineName}'. DPAPI-protected keys may not decrypt. " +
                    "All existing sessions will be invalidated.");
            }

            PendingDataRestore = true;
            Console.WriteLine("[DeploymentRestore] Phase 1 complete. Phase 2 will restore database and files after builder.Build().");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DeploymentRestore] Phase 1 failed (non-fatal): {ex.Message}");
            PendingDataRestore = false;
        }
    }

    // ================================================================
    // PHASE 2 EXPORT: Creates a portable, self-contained export package
    // ================================================================

    public async Task<ExportResult> ExportAsync(int userId, string userEmail, string? exportPath = null)
    {
        exportPath ??= ExportPath;

        // Guard: only one export at a time
        if (!await _exportLock.WaitAsync(0))
        {
            return new ExportResult(false, "An export is already in progress.", null);
        }

        string? tempDir = null;
        try
        {
            // 1. Validate required config file exists
            var configSource = Path.Combine(_env.ContentRootPath, "appsettings.Production.json");
            if (!File.Exists(configSource))
            {
                return new ExportResult(false,
                    $"appsettings.Production.json not found at {configSource}. Cannot export without production configuration.",
                    null);
            }

            // 2. Resolve DB path
            var connectionString = _configuration["ConnectionStrings:Default"] ?? "Data Source=app.db";
            var dbPath = DatabaseBackupService.ExtractDbPath(connectionString);
            if (!File.Exists(dbPath))
            {
                return new ExportResult(false, $"Database file not found at {dbPath}.", null);
            }

            // 3. Create temp staging directory
            var guid8 = Guid.NewGuid().ToString("N")[..8];
            tempDir = $"{exportPath}.new-{guid8}";
            Directory.CreateDirectory(tempDir);

            // 4. VACUUM INTO temp dir (creates a clean, consistent snapshot)
            var exportedDbPath = Path.GetFullPath(Path.Combine(tempDir, "app.db"));
            using (var conn = new SqliteConnection(connectionString))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "VACUUM INTO @dest;";
                cmd.Parameters.AddWithValue("@dest", exportedDbPath);
                await Task.Run(() => cmd.ExecuteNonQuery());
            }

            // 5. Compute SHA256 of exported DB
            var dbSha256 = await ComputeSha256Async(exportedDbPath);
            var dbFileInfo = new FileInfo(exportedDbPath);

            // 6. Copy avatars recursively
            var avatarsSource = Path.Combine(_env.WebRootPath, "avatars");
            var avatarCount = 0;
            if (Directory.Exists(avatarsSource))
            {
                avatarCount = CopyDirectoryRecursive(avatarsSource, Path.Combine(tempDir, "avatars"));
            }

            // 7. Copy feedback recursively
            var feedbackSource = Path.Combine(_env.WebRootPath, "feedback");
            var feedbackCount = 0;
            if (Directory.Exists(feedbackSource))
            {
                feedbackCount = CopyDirectoryRecursive(feedbackSource, Path.Combine(tempDir, "feedback"));
            }

            // 8. Copy DataProtection keys
            var dpSourceDir = Path.Combine(AppContext.BaseDirectory, "DataProtection-Keys");
            var dpKeyCount = 0;
            if (Directory.Exists(dpSourceDir))
            {
                dpKeyCount = CopyDirectoryRecursive(dpSourceDir, Path.Combine(tempDir, "DataProtection-Keys"));
            }

            // 9. Copy appsettings.Production.json
            File.Copy(configSource, Path.Combine(tempDir, "appsettings.Production.json"), overwrite: true);

            // 10. Get last migration ID from the exported DB
            //     IMPORTANT: Connection MUST be fully closed before Directory.Move later.
            //     Using a scoped block (not `using var`) + ClearAllPools to release all file handles.
            var lastMigrationId = string.Empty;
            try
            {
                using (var verifyConn = new SqliteConnection($"Data Source={exportedDbPath};Mode=ReadOnly"))
                {
                    verifyConn.Open();
                    using (var migCmd = verifyConn.CreateCommand())
                    {
                        migCmd.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1;";
                        var migResult = migCmd.ExecuteScalar();
                        if (migResult is string migId)
                            lastMigrationId = migId;
                    }
                }
                // Release pooled connections so no file handles remain open on tempDir
                SqliteConnection.ClearAllPools();
            }
            catch
            {
                // Table may not exist in test environments — non-fatal
            }

            // 11. Get app version from assembly
            var appVersion = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
                ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                ?? "unknown";

            // 12. Write manifest LAST (its presence signals a complete export)
            var manifest = new ExportManifest
            {
                ExportedAt = DateTime.UtcNow,
                AppVersion = appVersion,
                LastMigrationId = lastMigrationId,
                ExportedBy = userEmail,
                MachineName = Environment.MachineName,
                Database = new DatabaseInfo
                {
                    FileName = "app.db",
                    SizeBytes = dbFileInfo.Length,
                    Sha256 = dbSha256
                },
                AvatarCount = avatarCount,
                FeedbackImageCount = feedbackCount,
                DataProtectionKeyCount = dpKeyCount,
                DataProtectionMachineScoped = true,
                ConfigIncluded = true
            };

            var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(tempDir, "manifest.json"), manifestJson);

            // 13. Atomic four-step rename: old → .old-{guid}, new → final, delete old
            //     Release ALL pooled SQLite connections first — any connection that touched
            //     files inside tempDir (VACUUM INTO output, migration check) would block
            //     Directory.Move on Windows.
            SqliteConnection.ClearAllPools();

            var oldGuid = Guid.NewGuid().ToString("N")[..8];
            var oldBackupDir = $"{exportPath}.old-{oldGuid}";

            if (Directory.Exists(exportPath))
            {
                Directory.Move(exportPath, oldBackupDir);
            }

            Directory.Move(tempDir, exportPath);
            tempDir = null; // Ownership transferred — don't delete in finally

            if (Directory.Exists(oldBackupDir))
            {
                try { Directory.Delete(oldBackupDir, recursive: true); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not delete old export backup at {Path}", oldBackupDir);
                }
            }

            // 14. Clean up any orphaned .new-* and .old-* siblings
            CleanupOrphanedFolders(exportPath);

            // 15. Audit log (scoped service — resolve via IServiceScopeFactory)
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var auditLog = scope.ServiceProvider.GetService<IAuditLogService>();
                if (auditLog != null)
                {
                    await auditLog.LogUserActionAsync(
                        userId,
                        "DeploymentExport",
                        "System",
                        null,
                        $"Deployment export created by {userEmail}",
                        $"Version: {appVersion}, Migration: {lastMigrationId}, Avatars: {avatarCount}, DB SHA256: {dbSha256}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Audit log for export failed — export itself succeeded");
            }

            _logger.LogInformation(
                "Deployment export completed: {Path}, version={Version}, avatars={Avatars}, dbSha256={Sha256}",
                exportPath, appVersion, avatarCount, dbSha256);

            return new ExportResult(true, null, manifest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deployment export failed");

            // Clean up temp dir; previous export at exportPath is untouched
            if (tempDir != null && Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning(cleanupEx, "Failed to clean up temp export directory: {Path}", tempDir);
                }
            }

            return new ExportResult(false, ex.Message, null);
        }
        finally
        {
            _exportLock.Release();
        }
    }

    public ExportManifest? GetLastExportInfo(string? exportPath = null)
    {
        exportPath ??= ExportPath;
        var manifestPath = Path.Combine(exportPath, "manifest.json");
        if (!File.Exists(manifestPath))
            return null;

        try
        {
            var json = File.ReadAllText(manifestPath);
            return JsonSerializer.Deserialize<ExportManifest>(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read manifest.json from {Path}", manifestPath);
            return null;
        }
    }

    // ================================================================
    // PHASE 2: Restore database and files after WebApplication.Build()
    // Called from Program.cs BEFORE app.Run() when PendingDataRestore is true
    // ================================================================
    public async Task RestoreDataAsync(IServiceProvider serviceProvider)
    {
        if (!PendingDataRestore) return;

        var logger = serviceProvider.GetRequiredService<ILogger<DeploymentExportService>>();
        var env = serviceProvider.GetRequiredService<IWebHostEnvironment>();
        var config = serviceProvider.GetRequiredService<IConfiguration>();

        var connectionString = config["ConnectionStrings:Default"] ?? "Data Source=app.db";
        var dbTargetPath = Path.GetFullPath(DatabaseBackupService.ExtractDbPath(connectionString));

        string[] assemblyMigrations;
        using (var scope = serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            assemblyMigrations = db.Database.GetMigrations().ToArray();
        }

        await RestoreDataStaticAsync(ExportPath, dbTargetPath, env.WebRootPath, assemblyMigrations, logger);
    }

    /// <summary>
    /// Static Phase 2 implementation — testable without DI.
    /// Validates hash + migration compatibility, then copies DB and files.
    /// </summary>
    public static async Task RestoreDataStaticAsync(
        string exportPath,
        string dbTargetPath,
        string webRootPath,
        string[] assemblyMigrations,
        ILogger? logger = null)
    {
        if (!PendingDataRestore) return;

        try
        {
            // 1. Parse manifest
            var manifestPath = Path.Combine(exportPath, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                logger?.LogWarning("[DeploymentRestore] Phase 2: manifest.json not found at {Path} — skipping restore", manifestPath);
                return;
            }

            ExportManifest manifest;
            try
            {
                var json = await File.ReadAllTextAsync(manifestPath);
                var parsed = JsonSerializer.Deserialize<ExportManifest>(json);
                if (parsed == null)
                {
                    logger?.LogError("[DeploymentRestore] Phase 2: manifest.json parsed as null — skipping restore");
                    return;
                }
                manifest = parsed;
            }
            catch (JsonException ex)
            {
                logger?.LogError(ex, "[DeploymentRestore] Phase 2: Failed to parse manifest.json — skipping restore");
                return;
            }

            var exportedDbPath = Path.Combine(exportPath, "app.db");
            if (!File.Exists(exportedDbPath))
            {
                logger?.LogError("[DeploymentRestore] Phase 2: app.db not found in export — skipping restore");
                return;
            }

            // 2. Validate SHA256
            var actualHash = await ComputeSha256Async(exportedDbPath);
            if (!string.Equals(actualHash, manifest.Database.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                logger?.LogError(
                    "[DeploymentRestore] Phase 2: SHA256 mismatch — expected {Expected}, got {Actual} — skipping restore",
                    manifest.Database.Sha256, actualHash);
                return;
            }

            // 3. Migration compatibility — prevent downgrade
            if (!string.IsNullOrEmpty(manifest.LastMigrationId) &&
                assemblyMigrations.Length > 0 &&
                !assemblyMigrations.Contains(manifest.LastMigrationId, StringComparer.OrdinalIgnoreCase))
            {
                logger?.LogError(
                    "[DeploymentRestore] Phase 2: Migration '{MigrationId}' in export not found in this assembly — skipping restore to prevent downgrade",
                    manifest.LastMigrationId);
                return;
            }

            // 4. Clear pooled connections so Windows releases all file handles
            SqliteConnection.ClearAllPools();

            // 5. Copy app.db → target (overwrite)
            var dbTargetDir = Path.GetDirectoryName(dbTargetPath);
            if (!string.IsNullOrEmpty(dbTargetDir))
                Directory.CreateDirectory(dbTargetDir);

            File.Copy(exportedDbPath, dbTargetPath, overwrite: true);
            logger?.LogInformation("[DeploymentRestore] Phase 2: Restored app.db to {Target}", dbTargetPath);

            // 6. Delete stale WAL and SHM sidecar files at target location
            var walPath = dbTargetPath + "-wal";
            var shmPath = dbTargetPath + "-shm";
            if (File.Exists(walPath)) File.Delete(walPath);
            if (File.Exists(shmPath)) File.Delete(shmPath);

            // 7. Recursive copy avatars/ → webRootPath/avatars
            var avatarsSource = Path.Combine(exportPath, "avatars");
            var avatarsCopied = 0;
            if (Directory.Exists(avatarsSource))
            {
                avatarsCopied = CopyDirectoryRecursive(avatarsSource, Path.Combine(webRootPath, "avatars"));
                logger?.LogInformation("[DeploymentRestore] Phase 2: Restored {Count} avatar file(s)", avatarsCopied);
            }

            // 8. Recursive copy feedback/ → webRootPath/feedback
            var feedbackSource = Path.Combine(exportPath, "feedback");
            if (Directory.Exists(feedbackSource))
            {
                var feedbackCopied = CopyDirectoryRecursive(feedbackSource, Path.Combine(webRootPath, "feedback"));
                logger?.LogInformation("[DeploymentRestore] Phase 2: Restored {Count} feedback file(s)", feedbackCopied);
            }

            // 9. Validate avatar count vs manifest
            if (avatarsCopied != manifest.AvatarCount)
            {
                logger?.LogWarning(
                    "[DeploymentRestore] Phase 2: Avatar count mismatch — manifest says {Expected}, copied {Actual}",
                    manifest.AvatarCount, avatarsCopied);
            }

            // 10. Rename export folder → {exportPath}.restored-{timestamp}
            //     ClearAllPools again to release any connections opened during migration read
            SqliteConnection.ClearAllPools();

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var restoredPath = $"{exportPath}.restored-{timestamp}";
            try
            {
                Directory.Move(exportPath, restoredPath);
                logger?.LogInformation("[DeploymentRestore] Phase 2: Export folder renamed to {Path}", restoredPath);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "[DeploymentRestore] Phase 2: Could not rename export folder — non-fatal");
            }

            // 11. Clean up old .restored-* folders (keep 2 most recent)
            CleanupRestoredFolders(exportPath, logger);

            // 12. Clean up orphaned .old-* and .new-* folders
            CleanupOrphanedFoldersStatic(exportPath, logger);

            // 13. Signal Phase 2 complete
            RestoreJustCompleted = true;
            logger?.LogInformation("[DeploymentRestore] Phase 2 complete — application is running on restored data");
        }
        finally
        {
            PendingDataRestore = false;
        }
    }

    // ================================================================
    // Private helpers
    // ================================================================

    /// <summary>
    /// Recursively copies all files from sourceDir to destDir.
    /// Uses Path.GetRelativePath for correct cross-platform path mapping.
    /// Returns the number of files copied.
    /// </summary>
    private static int CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, file);
            var destFile = Path.Combine(destDir, relativePath);
            var destFileDir = Path.GetDirectoryName(destFile)!;
            Directory.CreateDirectory(destFileDir);
            File.Copy(file, destFile, overwrite: true);
            count++;
        }
        return count;
    }

    /// <summary>Computes a lowercase SHA256 hex string for the given file.</summary>
    private static async Task<string> ComputeSha256Async(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = await sha256.ComputeHashAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Deletes any orphaned .old-* and .new-* sibling directories left by
    /// interrupted previous export runs. Instance wrapper delegates to static version.
    /// </summary>
    private void CleanupOrphanedFolders(string finalExportPath)
        => CleanupOrphanedFoldersStatic(finalExportPath, _logger);

    /// <summary>
    /// Static version of CleanupOrphanedFolders — usable without a service instance.
    /// </summary>
    private static void CleanupOrphanedFoldersStatic(string finalExportPath, ILogger? logger = null)
    {
        try
        {
            var basePath = Path.GetDirectoryName(finalExportPath);
            var baseName = Path.GetFileName(finalExportPath);
            if (basePath == null) return;

            foreach (var dir in Directory.EnumerateDirectories(basePath, $"{baseName}.old-*")
                .Concat(Directory.EnumerateDirectories(basePath, $"{baseName}.new-*")))
            {
                try { Directory.Delete(dir, recursive: true); }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Could not delete orphaned export folder: {Dir}", dir);
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "CleanupOrphanedFolders failed for {Path}", finalExportPath);
        }
    }

    /// <summary>
    /// Keeps only the <see cref="MaxRestoredFoldersRetained"/> most recent .restored-* sibling
    /// folders next to the export path; deletes the rest by LastWriteTime ascending.
    /// </summary>
    private static void CleanupRestoredFolders(string exportPath, ILogger? logger = null)
    {
        try
        {
            var basePath = Path.GetDirectoryName(exportPath);
            var baseName = Path.GetFileName(exportPath);
            if (basePath == null) return;

            var restoredDirs = Directory.EnumerateDirectories(basePath, $"{baseName}.restored-*")
                .Select(d => new DirectoryInfo(d))
                .OrderByDescending(d => d.LastWriteTimeUtc)
                .ToList();

            foreach (var dir in restoredDirs.Skip(MaxRestoredFoldersRetained))
            {
                try { Directory.Delete(dir.FullName, recursive: true); }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Could not delete old restored folder: {Dir}", dir.FullName);
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "CleanupRestoredFolders failed for {Path}", exportPath);
        }
    }
}
