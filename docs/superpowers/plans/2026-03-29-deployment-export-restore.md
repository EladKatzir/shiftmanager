# Deployment Export/Restore Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable safe air-gapped updates by exporting all persistent data to `C:\ShiftManager\DeploymentExport\` and auto-restoring on startup after a file replacement deploy.

**Architecture:** A `DeploymentExportService` with static Phase 1 (pre-builder config/DP key restore) and instance Phase 2 (post-build DB/file restore). Export triggered from the existing Owner Backup page. Atomic folder writes via temp-then-rename.

**Tech Stack:** ASP.NET Core 8.0, SQLite (VACUUM INTO), Razor Pages, System.Text.Json, System.Security.Cryptography (SHA256)

**Spec:** `docs/superpowers/specs/2026-03-29-deployment-export-restore-design.md`

---

## File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `Services/IDeploymentExportService.cs` | Create | Interface: `ExportAsync`, `GetLastExportInfo`, `RestoreDataAsync` |
| `Services/DeploymentExportService.cs` | Create | All export/restore logic, static Phase 1, singleton Phase 2 |
| `Program.cs` | Modify | Phase 1 call at line 25, Phase 2 call after line 457, service registration |
| `Pages/Owner/Backup.cshtml.cs` | Modify | Inject service, add `OnPostPrepareForUpdateAsync`, add `LastExportInfo` property |
| `Pages/Owner/Backup.cshtml` | Modify | Add "Prepare for Update" card above existing backup actions |
| `ShiftManager.Tests/UnitTests/Services/DeploymentExportServiceTests.cs` | Create | Unit tests for export/restore logic |

---

### Task 1: Create the ExportManifest model and IDeploymentExportService interface

**Files:**
- Create: `Services/IDeploymentExportService.cs`

- [ ] **Step 1: Create the interface and model file**

```csharp
// Services/IDeploymentExportService.cs
using System.Text.Json.Serialization;

namespace ShiftManager.Services;

public interface IDeploymentExportService
{
    Task<ExportResult> ExportAsync(int userId, string userEmail);
    Task RestoreDataAsync(IServiceProvider serviceProvider);
    ExportManifest? GetLastExportInfo();
}

public record ExportResult(
    bool Success,
    string? ErrorMessage,
    ExportManifest? Manifest);

public class ExportManifest
{
    [JsonPropertyName("exportedAt")]
    public DateTime ExportedAt { get; set; }

    [JsonPropertyName("appVersion")]
    public string AppVersion { get; set; } = string.Empty;

    [JsonPropertyName("lastMigrationId")]
    public string LastMigrationId { get; set; } = string.Empty;

    [JsonPropertyName("exportedBy")]
    public string ExportedBy { get; set; } = string.Empty;

    [JsonPropertyName("machineName")]
    public string MachineName { get; set; } = string.Empty;

    [JsonPropertyName("database")]
    public DatabaseInfo Database { get; set; } = new();

    [JsonPropertyName("avatarCount")]
    public int AvatarCount { get; set; }

    [JsonPropertyName("feedbackImageCount")]
    public int FeedbackImageCount { get; set; }

    [JsonPropertyName("dataProtectionKeyCount")]
    public int DataProtectionKeyCount { get; set; }

    [JsonPropertyName("dataProtectionMachineScoped")]
    public bool DataProtectionMachineScoped { get; set; }

    [JsonPropertyName("configIncluded")]
    public bool ConfigIncluded { get; set; }
}

public class DatabaseInfo
{
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = "app.db";

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build ShiftManager.csproj --no-restore 2>&1 | tail -5`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add Services/IDeploymentExportService.cs
git commit -m "feat: add IDeploymentExportService interface and ExportManifest model"
```

---

### Task 2: Create DeploymentExportService — Static Phase 1 (RestoreConfigAndKeys)

**Files:**
- Create: `Services/DeploymentExportService.cs`

This task implements ONLY the static Phase 1 method and constants. Phase 2 and Export are added in later tasks.

- [ ] **Step 1: Write the test file with Phase 1 tests**

Create `ShiftManager.Tests/UnitTests/Services/DeploymentExportServiceTests.cs`:

```csharp
using FluentAssertions;
using System.Text.Json;

namespace ShiftManager.Tests.UnitTests.Services;

public class DeploymentExportServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _exportDir;
    private readonly string _appBaseDir;

    public DeploymentExportServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"deploy-test-{Guid.NewGuid():N}");
        _exportDir = Path.Combine(_testDir, "DeploymentExport");
        _appBaseDir = Path.Combine(_testDir, "AppBase");
        Directory.CreateDirectory(_exportDir);
        Directory.CreateDirectory(_appBaseDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, recursive: true); }
        catch { /* cleanup best-effort */ }
    }

    private void WriteManifest(ExportManifest? manifest = null)
    {
        manifest ??= new ExportManifest
        {
            ExportedAt = DateTime.UtcNow,
            AppVersion = "1.0.0",
            LastMigrationId = "20260326_Test",
            ExportedBy = "test@test.com",
            MachineName = Environment.MachineName,
            Database = new DatabaseInfo { FileName = "app.db", SizeBytes = 1024, Sha256 = "abc" },
            AvatarCount = 0,
            FeedbackImageCount = 0,
            DataProtectionKeyCount = 0,
            DataProtectionMachineScoped = true,
            ConfigIncluded = true
        };
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(_exportDir, "manifest.json"), json);
    }

    // --- Phase 1: RestoreConfigAndKeys ---

    [Fact]
    public void RestoreConfigAndKeys_NoExportFolder_NoPendingRestore()
    {
        var nonExistentPath = Path.Combine(_testDir, "DoesNotExist");

        DeploymentExportService.RestoreConfigAndKeys(nonExistentPath, _appBaseDir);

        DeploymentExportService.PendingDataRestore.Should().BeFalse();
    }

    [Fact]
    public void RestoreConfigAndKeys_NoManifest_NoPendingRestore()
    {
        // Export dir exists but no manifest.json
        DeploymentExportService.RestoreConfigAndKeys(_exportDir, _appBaseDir);

        DeploymentExportService.PendingDataRestore.Should().BeFalse();
    }

    [Fact]
    public void RestoreConfigAndKeys_ValidManifest_CopiesConfigAndSetsFlag()
    {
        WriteManifest();
        var configContent = "{\"ConnectionStrings\":{\"Default\":\"Data Source=test.db\"}}";
        File.WriteAllText(Path.Combine(_exportDir, "appsettings.Production.json"), configContent);

        DeploymentExportService.RestoreConfigAndKeys(_exportDir, _appBaseDir);

        DeploymentExportService.PendingDataRestore.Should().BeTrue();
        var restoredConfig = Path.Combine(_appBaseDir, "appsettings.Production.json");
        File.Exists(restoredConfig).Should().BeTrue();
        File.ReadAllText(restoredConfig).Should().Be(configContent);
    }

    [Fact]
    public void RestoreConfigAndKeys_CopiesDataProtectionKeys()
    {
        WriteManifest();
        File.WriteAllText(Path.Combine(_exportDir, "appsettings.Production.json"), "{}");
        var dpDir = Path.Combine(_exportDir, "DataProtection-Keys");
        Directory.CreateDirectory(dpDir);
        File.WriteAllText(Path.Combine(dpDir, "key-1.xml"), "<xml>key1</xml>");
        File.WriteAllText(Path.Combine(dpDir, "key-2.xml"), "<xml>key2</xml>");

        DeploymentExportService.RestoreConfigAndKeys(_exportDir, _appBaseDir);

        DeploymentExportService.PendingDataRestore.Should().BeTrue();
        var restoredDpDir = Path.Combine(_appBaseDir, "DataProtection-Keys");
        Directory.Exists(restoredDpDir).Should().BeTrue();
        Directory.GetFiles(restoredDpDir, "*.xml").Length.Should().Be(2);
    }

    [Fact]
    public void RestoreConfigAndKeys_CorruptManifest_NoPendingRestore()
    {
        File.WriteAllText(Path.Combine(_exportDir, "manifest.json"), "NOT VALID JSON{{{");

        DeploymentExportService.RestoreConfigAndKeys(_exportDir, _appBaseDir);

        DeploymentExportService.PendingDataRestore.Should().BeFalse();
    }

    [Fact]
    public void RestoreConfigAndKeys_NeverThrows_EvenOnIOError()
    {
        WriteManifest();
        // No appsettings.Production.json in export — File.Copy will fail
        // Phase 1 must catch and return normally

        var act = () => DeploymentExportService.RestoreConfigAndKeys(_exportDir, _appBaseDir);

        act.Should().NotThrow();
        // PendingDataRestore could be true or false depending on where the error happened,
        // but the key point is it did not throw
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ShiftManager.Tests --filter "FullyQualifiedName~DeploymentExportServiceTests" --no-build 2>&1 | tail -10`
Expected: Build failure — `DeploymentExportService` does not exist yet.

- [ ] **Step 3: Create the service with Phase 1 implementation**

Create `Services/DeploymentExportService.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text.Json;

namespace ShiftManager.Services;

public class DeploymentExportService : IDeploymentExportService
{
    public const string ExportPath = @"C:\ShiftManager\DeploymentExport";
    public const int MaxRestoredFoldersRetained = 2;

    private static readonly SemaphoreSlim _exportLock = new(1, 1);

    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DeploymentExportService> _logger;
    private readonly IAuditLogService _auditLogService;

    // Static flags for two-phase restore
    public static bool PendingDataRestore { get; set; }
    public static volatile bool RestoreJustCompleted;

    public DeploymentExportService(
        IWebHostEnvironment env,
        IConfiguration configuration,
        ILogger<DeploymentExportService> logger,
        IAuditLogService auditLogService)
    {
        _env = env;
        _configuration = configuration;
        _logger = logger;
        _auditLogService = auditLogService;
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

            // Parse manifest
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

            // Copy appsettings.Production.json
            var configSource = Path.Combine(exportPath, "appsettings.Production.json");
            if (File.Exists(configSource))
            {
                var configDest = Path.Combine(appBaseDir, "appsettings.Production.json");
                File.Copy(configSource, configDest, overwrite: true);
                Console.WriteLine($"[DeploymentRestore] Restored appsettings.Production.json");
            }

            // Copy DataProtection keys
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

            // DPAPI machine-scope warning
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

    // Phase 2 and ExportAsync are implemented in subsequent tasks.
    // Placeholder signatures to satisfy the interface:
    public Task<ExportResult> ExportAsync(int userId, string userEmail)
        => throw new NotImplementedException("Implemented in Task 3");

    public Task RestoreDataAsync(IServiceProvider serviceProvider)
        => throw new NotImplementedException("Implemented in Task 4");

    public ExportManifest? GetLastExportInfo()
        => throw new NotImplementedException("Implemented in Task 3");
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test ShiftManager.Tests --filter "FullyQualifiedName~DeploymentExportServiceTests" -v normal 2>&1 | tail -15`
Expected: All 6 tests pass.

- [ ] **Step 5: Commit**

```bash
git add Services/DeploymentExportService.cs ShiftManager.Tests/UnitTests/Services/DeploymentExportServiceTests.cs
git commit -m "feat: add DeploymentExportService Phase 1 — static pre-builder config/DP key restore"
```

---

### Task 3: Implement ExportAsync and GetLastExportInfo

**Files:**
- Modify: `Services/DeploymentExportService.cs`
- Modify: `ShiftManager.Tests/UnitTests/Services/DeploymentExportServiceTests.cs`

- [ ] **Step 1: Add export tests to the test file**

Append to `DeploymentExportServiceTests.cs`. These tests require a full service instance. Add a helper method and new test class section:

```csharp
// Add at the end of the test file, after the existing class closing brace.
// This is a separate test class for ExportAsync since it needs DI mocks.

public class DeploymentExportServiceExportTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _webRootPath;
    private readonly string _contentRootPath;
    private readonly string _exportDir;

    public DeploymentExportServiceExportTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"export-test-{Guid.NewGuid():N}");
        _webRootPath = Path.Combine(_testDir, "wwwroot");
        _contentRootPath = Path.Combine(_testDir, "app");
        _exportDir = Path.Combine(_testDir, "DeploymentExport");
        Directory.CreateDirectory(_webRootPath);
        Directory.CreateDirectory(_contentRootPath);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, recursive: true); }
        catch { /* cleanup best-effort */ }
    }

    private DeploymentExportService CreateService()
    {
        var envMock = new Mock<IWebHostEnvironment>();
        envMock.Setup(e => e.WebRootPath).Returns(_webRootPath);
        envMock.Setup(e => e.ContentRootPath).Returns(_contentRootPath);

        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c.GetSection("ConnectionStrings")["Default"])
            .Returns($"Data Source={Path.Combine(_testDir, "data", "app.db")}");

        var loggerMock = Mock.Of<ILogger<DeploymentExportService>>();
        var auditMock = new Mock<IAuditLogService>();

        return new DeploymentExportService(envMock.Object, configMock.Object, loggerMock, auditMock.Object);
    }

    [Fact]
    public async Task ExportAsync_MissingProductionConfig_ReturnsError()
    {
        var service = CreateService();
        // No appsettings.Production.json in content root

        var result = await service.ExportAsync(1, "test@test.com", _exportDir);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("appsettings.Production.json");
    }

    [Fact]
    public async Task ExportAsync_CreatesManifestWithCorrectCounts()
    {
        var service = CreateService();

        // Create appsettings.Production.json
        File.WriteAllText(Path.Combine(_contentRootPath, "appsettings.Production.json"), "{}");

        // Create test database (the service uses VACUUM INTO but for tests we need a real SQLite DB)
        var dbDir = Path.Combine(_testDir, "data");
        Directory.CreateDirectory(dbDir);
        var dbPath = Path.Combine(dbDir, "app.db");
        using (var conn = new SqliteConnection($"Data Source={dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE test (id INTEGER PRIMARY KEY);";
            cmd.ExecuteNonQuery();
        }

        // Create avatars
        var avatarDir = Path.Combine(_webRootPath, "avatars", "1");
        Directory.CreateDirectory(avatarDir);
        File.WriteAllText(Path.Combine(avatarDir, "5.jpg"), "fake-avatar");
        File.WriteAllText(Path.Combine(avatarDir, "5_thumb.jpg"), "fake-thumb");

        var result = await service.ExportAsync(1, "test@test.com", _exportDir);

        result.Success.Should().BeTrue();
        result.Manifest.Should().NotBeNull();
        result.Manifest!.AvatarCount.Should().Be(2);
        result.Manifest.ConfigIncluded.Should().BeTrue();
        result.Manifest.MachineName.Should().Be(Environment.MachineName);

        // Verify manifest file exists in the export directory
        File.Exists(Path.Combine(_exportDir, "manifest.json")).Should().BeTrue();
        // Verify the DB was exported
        File.Exists(Path.Combine(_exportDir, "app.db")).Should().BeTrue();
    }

    [Fact]
    public async Task ExportAsync_AtomicReplace_PreservesPreviousOnFailure()
    {
        var service = CreateService();

        // Create a "previous" export with a marker file
        Directory.CreateDirectory(_exportDir);
        File.WriteAllText(Path.Combine(_exportDir, "previous-marker.txt"), "old");
        // Write a valid manifest so it looks like a real previous export
        File.WriteAllText(Path.Combine(_exportDir, "manifest.json"), "{}");

        // Now try to export WITHOUT the required appsettings.Production.json — should fail
        var result = await service.ExportAsync(1, "test@test.com", _exportDir);

        result.Success.Should().BeFalse();
        // Previous export should still be intact
        File.Exists(Path.Combine(_exportDir, "previous-marker.txt")).Should().BeTrue();
    }

    [Fact]
    public void GetLastExportInfo_NoExportFolder_ReturnsNull()
    {
        var service = CreateService();
        var info = service.GetLastExportInfo(_exportDir);
        info.Should().BeNull();
    }

    [Fact]
    public void GetLastExportInfo_ValidManifest_ReturnsManifest()
    {
        var service = CreateService();
        Directory.CreateDirectory(_exportDir);
        var manifest = new ExportManifest { ExportedAt = DateTime.UtcNow, ExportedBy = "test@test.com" };
        File.WriteAllText(
            Path.Combine(_exportDir, "manifest.json"),
            JsonSerializer.Serialize(manifest));

        var info = service.GetLastExportInfo(_exportDir);

        info.Should().NotBeNull();
        info!.ExportedBy.Should().Be("test@test.com");
    }
}
```

Note: Add `using Microsoft.Data.Sqlite;` and `using Moq;` and `using ShiftManager.Services;` to the test file imports.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ShiftManager.Tests --filter "FullyQualifiedName~DeploymentExportService" --no-build 2>&1 | tail -10`
Expected: Compilation errors or `NotImplementedException`.

- [ ] **Step 3: Implement ExportAsync in DeploymentExportService.cs**

Replace the placeholder `ExportAsync` and `GetLastExportInfo` methods with the full implementation. The `ExportAsync` method signature gains an optional `exportPath` parameter for testability:

```csharp
public async Task<ExportResult> ExportAsync(int userId, string userEmail, string? exportPath = null)
{
    exportPath ??= ExportPath;

    if (!await _exportLock.WaitAsync(TimeSpan.Zero))
        return new ExportResult(false, "Another export is already in progress.", null);

    try
    {
        _logger.LogInformation("Starting deployment export for user {Email}", userEmail);

        // Validate appsettings.Production.json exists (hard error)
        var configPath = Path.Combine(_env.ContentRootPath, "appsettings.Production.json");
        if (!File.Exists(configPath))
            return new ExportResult(false, "appsettings.Production.json not found in content root. Export requires production config.", null);

        // Resolve database path
        var connectionString = _configuration.GetConnectionString("Default") ?? "Data Source=app.db";
        var dbPath = DatabaseBackupService.ExtractDbPath(connectionString);
        var fullDbPath = Path.GetFullPath(dbPath);
        if (!File.Exists(fullDbPath))
            return new ExportResult(false, $"Database file not found at {fullDbPath}.", null);

        // Create temp directory for atomic write
        var tempGuid = Guid.NewGuid().ToString("N")[..8];
        var tempDir = $"{exportPath}.new-{tempGuid}";
        Directory.CreateDirectory(tempDir);

        try
        {
            // 1. VACUUM INTO for atomic DB snapshot
            var exportDbPath = Path.GetFullPath(Path.Combine(tempDir, "app.db"));
            using (var conn = new SqliteConnection(connectionString))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "VACUUM INTO @backupPath;";
                cmd.Parameters.AddWithValue("@backupPath", exportDbPath);
                await Task.Run(() => cmd.ExecuteNonQuery());
            }

            // 2. Compute SHA256 of exported DB
            var dbHash = await ComputeSha256Async(exportDbPath);
            var dbSize = new FileInfo(exportDbPath).Length;

            // 3. Copy avatars
            var avatarCount = 0;
            var avatarSourceDir = Path.Combine(_env.WebRootPath, "avatars");
            if (Directory.Exists(avatarSourceDir))
            {
                avatarCount = CopyDirectoryRecursive(avatarSourceDir, Path.Combine(tempDir, "avatars"));
            }

            // 4. Copy feedback images
            var feedbackCount = 0;
            var feedbackSourceDir = Path.Combine(_env.WebRootPath, "feedback");
            if (Directory.Exists(feedbackSourceDir))
            {
                feedbackCount = CopyDirectoryRecursive(feedbackSourceDir, Path.Combine(tempDir, "feedback"));
            }

            // 5. Copy DataProtection keys
            var dpCount = 0;
            var dpSourceDir = Path.Combine(AppContext.BaseDirectory, "DataProtection-Keys");
            if (Directory.Exists(dpSourceDir))
            {
                var dpDestDir = Path.Combine(tempDir, "DataProtection-Keys");
                Directory.CreateDirectory(dpDestDir);
                foreach (var keyFile in Directory.GetFiles(dpSourceDir, "*.xml"))
                {
                    File.Copy(keyFile, Path.Combine(dpDestDir, Path.GetFileName(keyFile)));
                    dpCount++;
                }
            }

            // 6. Copy appsettings.Production.json
            File.Copy(configPath, Path.Combine(tempDir, "appsettings.Production.json"));

            // 7. Get last migration ID
            var lastMigrationId = "";
            try
            {
                using var scope = _logger.BeginScope("ExportMigrationCheck");
                // Read from the exported DB to get the applied migrations
                using var exportConn = new SqliteConnection($"Data Source={exportDbPath};Mode=ReadOnly");
                exportConn.Open();
                using var migCmd = exportConn.CreateCommand();
                migCmd.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1;";
                lastMigrationId = migCmd.ExecuteScalar()?.ToString() ?? "";
            }
            catch { /* DB might not have migrations table yet */ }

            // 8. Write manifest LAST (commit signal)
            var appVersion = typeof(DeploymentExportService).Assembly
                .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

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
                    SizeBytes = dbSize,
                    Sha256 = dbHash
                },
                AvatarCount = avatarCount,
                FeedbackImageCount = feedbackCount,
                DataProtectionKeyCount = dpCount,
                DataProtectionMachineScoped = OperatingSystem.IsWindows(),
                ConfigIncluded = true
            };

            var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(tempDir, "manifest.json"), manifestJson);

            // 9. Atomic four-step rename
            string? oldGuid = null;
            if (Directory.Exists(exportPath))
            {
                oldGuid = Guid.NewGuid().ToString("N")[..8];
                Directory.Move(exportPath, $"{exportPath}.old-{oldGuid}");
            }

            Directory.Move(tempDir, exportPath);

            if (oldGuid != null)
            {
                try { Directory.Delete($"{exportPath}.old-{oldGuid}", recursive: true); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete old export folder"); }
            }

            // Clean up any orphaned .new-* or .old-* folders
            CleanupOrphanedFolders(exportPath);

            // Audit log
            await _auditLogService.LogUserActionAsync(
                userId, "DeploymentExportCreated", "System", null,
                $"Deployment export created: {manifest.Database.SizeBytes / 1024}KB DB, {manifest.AvatarCount} avatars, {manifest.FeedbackImageCount} feedback images",
                null);

            _logger.LogInformation(
                "Deployment export completed: {DbSize}KB DB, {AvatarCount} avatars, {FeedbackCount} feedback, {DpCount} DP keys",
                dbSize / 1024, avatarCount, feedbackCount, dpCount);

            return new ExportResult(true, null, manifest);
        }
        catch
        {
            // Clean up temp folder on failure — previous export is untouched
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
            catch { /* best-effort cleanup */ }
            throw;
        }
    }
    catch (Exception ex) when (ex is not ExportResult)
    {
        _logger.LogError(ex, "Deployment export failed");
        return new ExportResult(false, $"Export failed: {ex.Message}", null);
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
    catch
    {
        return null;
    }
}

// --- Private helpers ---

private static int CopyDirectoryRecursive(string sourceDir, string destDir)
{
    var count = 0;
    foreach (var dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
    {
        Directory.CreateDirectory(dirPath.Replace(sourceDir, destDir));
    }
    foreach (var filePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
    {
        var destPath = filePath.Replace(sourceDir, destDir);
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        File.Copy(filePath, destPath, overwrite: true);
        count++;
    }
    return count;
}

private static async Task<string> ComputeSha256Async(string filePath)
{
    using var sha256 = SHA256.Create();
    using var stream = File.OpenRead(filePath);
    var hash = await sha256.ComputeHashAsync(stream);
    return Convert.ToHexString(hash).ToLowerInvariant();
}

private static void CleanupOrphanedFolders(string basePath)
{
    var parentDir = Path.GetDirectoryName(basePath);
    if (parentDir == null || !Directory.Exists(parentDir)) return;
    var baseName = Path.GetFileName(basePath);

    foreach (var dir in Directory.GetDirectories(parentDir, $"{baseName}.*"))
    {
        var name = Path.GetFileName(dir);
        if (name.Contains(".old-") || name.Contains(".new-"))
        {
            try { Directory.Delete(dir, recursive: true); }
            catch { /* best-effort */ }
        }
    }
}
```

Also update the interface to include the optional `exportPath` parameter:

```csharp
// In IDeploymentExportService.cs:
Task<ExportResult> ExportAsync(int userId, string userEmail, string? exportPath = null);
ExportManifest? GetLastExportInfo(string? exportPath = null);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test ShiftManager.Tests --filter "FullyQualifiedName~DeploymentExportService" -v normal 2>&1 | tail -15`
Expected: All tests pass (Phase 1 tests + new export tests).

- [ ] **Step 5: Commit**

```bash
git add Services/DeploymentExportService.cs Services/IDeploymentExportService.cs ShiftManager.Tests/UnitTests/Services/DeploymentExportServiceTests.cs
git commit -m "feat: implement ExportAsync with atomic folder writes and SHA256 validation"
```

---

### Task 4: Implement Phase 2 — RestoreDataAsync

**Files:**
- Modify: `Services/DeploymentExportService.cs`
- Modify: `ShiftManager.Tests/UnitTests/Services/DeploymentExportServiceTests.cs`

- [ ] **Step 1: Add Phase 2 restore tests**

Add a new test class to the test file:

```csharp
public class DeploymentExportServiceRestoreTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _exportDir;
    private readonly string _webRootPath;
    private readonly string _dbTargetDir;

    public DeploymentExportServiceRestoreTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"restore-test-{Guid.NewGuid():N}");
        _exportDir = Path.Combine(_testDir, "DeploymentExport");
        _webRootPath = Path.Combine(_testDir, "wwwroot");
        _dbTargetDir = Path.Combine(_testDir, "data");
        Directory.CreateDirectory(_exportDir);
        Directory.CreateDirectory(_webRootPath);
        Directory.CreateDirectory(_dbTargetDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, recursive: true); }
        catch { /* best-effort */ }
    }

    private string CreateTestDb(string dir, string name = "app.db")
    {
        var dbPath = Path.Combine(dir, name);
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE test (id INTEGER PRIMARY KEY); INSERT INTO test VALUES (42);";
        cmd.ExecuteNonQuery();
        // Create the migrations history table
        using var migCmd = conn.CreateCommand();
        migCmd.CommandText = @"
            CREATE TABLE __EFMigrationsHistory (MigrationId TEXT PRIMARY KEY, ProductVersion TEXT);
            INSERT INTO __EFMigrationsHistory VALUES ('20260326225317_AddStoresAndQuickInfoConfig', '8.0.0');";
        migCmd.ExecuteNonQuery();
        return dbPath;
    }

    private async Task<string> ComputeSha256(string path)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(path);
        var hash = await sha256.ComputeHashAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task WriteManifestForDb(string exportDbPath)
    {
        var hash = await ComputeSha256(exportDbPath);
        var manifest = new ExportManifest
        {
            ExportedAt = DateTime.UtcNow,
            AppVersion = "1.0.0",
            LastMigrationId = "20260326225317_AddStoresAndQuickInfoConfig",
            ExportedBy = "test@test.com",
            MachineName = Environment.MachineName,
            Database = new DatabaseInfo { FileName = "app.db", SizeBytes = new FileInfo(exportDbPath).Length, Sha256 = hash },
            AvatarCount = 0,
            ConfigIncluded = true
        };
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(_exportDir, "manifest.json"), json);
    }

    [Fact]
    public async Task RestoreDataAsync_ValidExport_RestoresDbAndFiles()
    {
        // Create export DB
        var exportDbPath = CreateTestDb(_exportDir);
        await WriteManifestForDb(exportDbPath);

        // Create exported avatars
        var avatarDir = Path.Combine(_exportDir, "avatars", "1");
        Directory.CreateDirectory(avatarDir);
        File.WriteAllText(Path.Combine(avatarDir, "5.jpg"), "avatar-data");

        var dbTargetPath = Path.Combine(_dbTargetDir, "app.db");

        // Simulate calling RestoreDataAsync
        DeploymentExportService.PendingDataRestore = true;
        await DeploymentExportService.RestoreDataStaticAsync(
            _exportDir, dbTargetPath, _webRootPath,
            new[] { "20260326225317_AddStoresAndQuickInfoConfig" });

        // DB was copied
        File.Exists(dbTargetPath).Should().BeTrue();
        // Avatar was copied
        File.Exists(Path.Combine(_webRootPath, "avatars", "1", "5.jpg")).Should().BeTrue();
        // Export folder was renamed
        Directory.Exists(_exportDir).Should().BeFalse();
        DeploymentExportService.RestoreJustCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task RestoreDataAsync_HashMismatch_SkipsRestore()
    {
        var exportDbPath = CreateTestDb(_exportDir);
        // Write manifest with wrong hash
        var manifest = new ExportManifest
        {
            ExportedAt = DateTime.UtcNow,
            LastMigrationId = "20260326225317_AddStoresAndQuickInfoConfig",
            Database = new DatabaseInfo { FileName = "app.db", SizeBytes = 100, Sha256 = "wrong-hash" },
            ConfigIncluded = true
        };
        File.WriteAllText(Path.Combine(_exportDir, "manifest.json"), JsonSerializer.Serialize(manifest));

        DeploymentExportService.PendingDataRestore = true;
        var dbTargetPath = Path.Combine(_dbTargetDir, "app.db");

        await DeploymentExportService.RestoreDataStaticAsync(
            _exportDir, dbTargetPath, _webRootPath,
            new[] { "20260326225317_AddStoresAndQuickInfoConfig" });

        // DB should NOT have been copied
        File.Exists(dbTargetPath).Should().BeFalse();
        // Export folder should still exist (not renamed)
        Directory.Exists(_exportDir).Should().BeTrue();
    }

    [Fact]
    public async Task RestoreDataAsync_MigrationDowngrade_SkipsRestore()
    {
        var exportDbPath = CreateTestDb(_exportDir);
        await WriteManifestForDb(exportDbPath);

        DeploymentExportService.PendingDataRestore = true;
        var dbTargetPath = Path.Combine(_dbTargetDir, "app.db");

        // Assembly migrations does NOT contain the export's lastMigrationId
        await DeploymentExportService.RestoreDataStaticAsync(
            _exportDir, dbTargetPath, _webRootPath,
            new[] { "20250101_OlderMigration" }); // doesn't include the export's migration

        File.Exists(dbTargetPath).Should().BeFalse();
        Directory.Exists(_exportDir).Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test ShiftManager.Tests --filter "FullyQualifiedName~DeploymentExportServiceRestoreTests" --no-build 2>&1 | tail -5`
Expected: `RestoreDataStaticAsync` does not exist yet.

- [ ] **Step 3: Implement RestoreDataAsync and RestoreDataStaticAsync**

Add to `DeploymentExportService.cs`. The instance method `RestoreDataAsync` resolves DI dependencies and delegates to the static `RestoreDataStaticAsync` (which is testable without DI):

```csharp
public async Task RestoreDataAsync(IServiceProvider serviceProvider)
{
    if (!PendingDataRestore) return;

    var logger = serviceProvider.GetRequiredService<ILogger<DeploymentExportService>>();
    var env = serviceProvider.GetRequiredService<IWebHostEnvironment>();
    var config = serviceProvider.GetRequiredService<IConfiguration>();

    var connectionString = config.GetConnectionString("Default") ?? "Data Source=app.db";
    var dbTargetPath = Path.GetFullPath(DatabaseBackupService.ExtractDbPath(connectionString));

    // Get assembly migration list (NOT applied migrations — no DB query)
    string[] assemblyMigrations;
    using (var scope = serviceProvider.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        assemblyMigrations = db.Database.GetMigrations().ToArray();
    }

    await RestoreDataStaticAsync(ExportPath, dbTargetPath, env.WebRootPath, assemblyMigrations, logger);
}

public static async Task RestoreDataStaticAsync(
    string exportPath, string dbTargetPath, string webRootPath,
    string[] assemblyMigrations, ILogger? logger = null)
{
    if (!PendingDataRestore) return;

    try
    {
        var manifestPath = Path.Combine(exportPath, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            logger?.LogError("Phase 2: manifest.json not found at {Path}. Skipping restore.", manifestPath);
            return;
        }

        var manifest = JsonSerializer.Deserialize<ExportManifest>(File.ReadAllText(manifestPath));
        if (manifest == null)
        {
            logger?.LogError("Phase 2: manifest.json parsed as null. Skipping restore.");
            return;
        }

        // Validate DB hash
        var exportDbPath = Path.Combine(exportPath, manifest.Database.FileName);
        if (!File.Exists(exportDbPath))
        {
            logger?.LogError("Phase 2: Database file {File} not found in export. Skipping restore.", manifest.Database.FileName);
            return;
        }

        using var sha256 = SHA256.Create();
        using (var stream = File.OpenRead(exportDbPath))
        {
            var hash = Convert.ToHexString(await sha256.ComputeHashAsync(stream)).ToLowerInvariant();
            if (hash != manifest.Database.Sha256)
            {
                logger?.LogError("Phase 2: Database SHA256 mismatch. Expected {Expected}, got {Actual}. Skipping restore.",
                    manifest.Database.Sha256, hash);
                return;
            }
        }

        // Migration compatibility check
        if (!string.IsNullOrEmpty(manifest.LastMigrationId) &&
            !assemblyMigrations.Contains(manifest.LastMigrationId))
        {
            logger?.LogError(
                "Phase 2: Export's last migration '{MigrationId}' not found in assembly. " +
                "The exported DB may be from a newer app version. Skipping restore to prevent schema incompatibility.",
                manifest.LastMigrationId);
            return;
        }

        logger?.LogInformation("Phase 2: Validation passed. Restoring data...");

        // Clear SQLite pools before overwriting DB
        SqliteConnection.ClearAllPools();

        // Copy DB
        var dbDir = Path.GetDirectoryName(dbTargetPath);
        if (!string.IsNullOrEmpty(dbDir)) Directory.CreateDirectory(dbDir);
        File.Copy(exportDbPath, dbTargetPath, overwrite: true);
        logger?.LogInformation("Phase 2: Restored database ({SizeKB}KB)", manifest.Database.SizeBytes / 1024);

        // Delete stale WAL/SHM from target
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var walPath = dbTargetPath + suffix;
            if (File.Exists(walPath))
            {
                try { File.Delete(walPath); }
                catch (Exception ex) { logger?.LogWarning(ex, "Could not delete {File}", walPath); }
            }
        }

        // Copy avatars
        var exportAvatarsDir = Path.Combine(exportPath, "avatars");
        var copiedAvatars = 0;
        if (Directory.Exists(exportAvatarsDir))
        {
            copiedAvatars = CopyDirectoryRecursive(exportAvatarsDir, Path.Combine(webRootPath, "avatars"));
        }

        // Copy feedback
        var exportFeedbackDir = Path.Combine(exportPath, "feedback");
        var copiedFeedback = 0;
        if (Directory.Exists(exportFeedbackDir))
        {
            copiedFeedback = CopyDirectoryRecursive(exportFeedbackDir, Path.Combine(webRootPath, "feedback"));
        }

        // Validate avatar count
        if (copiedAvatars != manifest.AvatarCount)
        {
            logger?.LogWarning("Phase 2: Avatar count mismatch. Expected {Expected}, copied {Actual}.",
                manifest.AvatarCount, copiedAvatars);
        }

        // Rename export folder
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var restoredPath = $"{exportPath}.restored-{timestamp}";
        Directory.Move(exportPath, restoredPath);
        logger?.LogInformation("Phase 2: Export folder renamed to {Path}", restoredPath);

        // Cleanup old .restored-* folders
        CleanupRestoredFolders(exportPath, logger);

        RestoreJustCompleted = true;
        logger?.LogInformation(
            "Phase 2: Deployment restore completed. DB: {DbKB}KB, Avatars: {Avatars}, Feedback: {Feedback}",
            manifest.Database.SizeBytes / 1024, copiedAvatars, copiedFeedback);
    }
    catch (Exception ex)
    {
        logger?.LogError(ex, "Phase 2: Restore failed");
    }
    finally
    {
        PendingDataRestore = false;
    }
}

private static void CleanupRestoredFolders(string basePath, ILogger? logger = null)
{
    var parentDir = Path.GetDirectoryName(basePath);
    if (parentDir == null || !Directory.Exists(parentDir)) return;
    var baseName = Path.GetFileName(basePath);

    var restoredDirs = Directory.GetDirectories(parentDir, $"{baseName}.restored-*")
        .Select(d => new DirectoryInfo(d))
        .OrderByDescending(d => d.LastWriteTime)
        .Skip(MaxRestoredFoldersRetained)
        .ToList();

    foreach (var dir in restoredDirs)
    {
        try
        {
            dir.Delete(recursive: true);
            logger?.LogInformation("Cleaned up old restored export: {Name}", dir.Name);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to delete old restored export: {Name}", dir.Name);
        }
    }

    // Also clean up orphaned .old-* and .new-* folders
    CleanupOrphanedFolders(basePath);
}
```

- [ ] **Step 4: Run all tests**

Run: `dotnet test ShiftManager.Tests --filter "FullyQualifiedName~DeploymentExportService" -v normal 2>&1 | tail -20`
Expected: All tests pass (Phase 1 + Export + Restore tests).

- [ ] **Step 5: Commit**

```bash
git add Services/DeploymentExportService.cs ShiftManager.Tests/UnitTests/Services/DeploymentExportServiceTests.cs
git commit -m "feat: implement Phase 2 RestoreDataAsync with hash validation and migration compatibility check"
```

---

### Task 5: Wire into Program.cs and register the service

**Files:**
- Modify: `Program.cs:25` (Phase 1 call)
- Modify: `Program.cs:~275` (service registration)
- Modify: `Program.cs:~458` (Phase 2 call)

- [ ] **Step 1: Add Phase 1 call before the builder (line 25)**

In `Program.cs`, insert between `try {` (line 24) and `var builder =` (line 26):

```csharp
// Deployment Export: Phase 1 — restore config + DataProtection keys before builder reads them
// This must run before WebApplication.CreateBuilder() which freezes IConfiguration and loads DP keys
DeploymentExportService.RestoreConfigAndKeys();
```

The `using ShiftManager.Services;` is already at line 10.

- [ ] **Step 2: Register the service as singleton**

In `Program.cs`, add after the existing service registrations (around line 275, near other singleton/scoped services):

```csharp
builder.Services.AddSingleton<IDeploymentExportService, DeploymentExportService>();
```

- [ ] **Step 3: Add Phase 2 call after builder.Build()**

In `Program.cs`, after `var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();` (line 460) and before the PRE-MIGRATION BACKUP block (line 462), insert:

```csharp
    // Deployment Export: Phase 2 — restore database + avatars + feedback
    if (DeploymentExportService.PendingDataRestore)
    {
        var exportService = app.Services.GetRequiredService<IDeploymentExportService>();
        await exportService.RestoreDataAsync(app.Services);
        logger.LogInformation("Deployment restore Phase 2 completed");
    }
```

- [ ] **Step 4: Verify it compiles and runs**

Run: `dotnet build ShiftManager.csproj --no-restore 2>&1 | tail -5`
Expected: `Build succeeded.`

- [ ] **Step 5: Run all tests to confirm no regressions**

Run: `dotnet test ShiftManager.Tests -v normal 2>&1 | tail -15`
Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add Program.cs
git commit -m "feat: wire DeploymentExportService into Program.cs — Phase 1 pre-builder, Phase 2 pre-migration"
```

---

### Task 6: Add export UI to the Owner Backup page

**Files:**
- Modify: `Pages/Owner/Backup.cshtml.cs`
- Modify: `Pages/Owner/Backup.cshtml`

- [ ] **Step 1: Add service injection and properties to BackupModel**

In `Backup.cshtml.cs`, add `IDeploymentExportService` to the constructor and new properties:

```csharp
// Add field:
private readonly IDeploymentExportService _exportService;

// Update constructor to accept IDeploymentExportService:
public BackupModel(
    IConfiguration configuration,
    IAuditLogService auditLogService,
    ILogger<BackupModel> logger,
    IDeploymentExportService exportService)
{
    _auditLogService = auditLogService;
    _logger = logger;
    _exportService = exportService;
    // ... existing code ...
}

// Add properties:
public ExportManifest? LastExportInfo { get; set; }
public bool ShowRestoreBanner { get; set; }
```

Update `OnGet()` to load export info:

```csharp
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
```

Add the export handler:

```csharp
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
```

- [ ] **Step 2: Add the export UI section to Backup.cshtml**

Insert the "Prepare for Update" card after the alert messages (line 42) and before the existing backup actions card (line 44):

```html
    @if (Model.ShowRestoreBanner)
    {
        <div class="alert alert-success">
            <strong>&#x2705; Data Restored:</strong>
            <loc key="Owner_Backup_RestoreBanner">Data was automatically restored from a deployment export. All data is intact.</loc>
        </div>
    }

    <div class="backup-actions-card" style="border-left: 4px solid var(--primary);">
        <h2>&#x1F680; <loc key="Owner_Backup_PrepareForUpdate">Prepare for Update</loc></h2>
        <p class="help-text" style="margin-top: 0;">
            <loc key="Owner_Backup_PrepareDesc">Export all persistent data (database, avatars, feedback images, config) to a safe location.
            After replacing the application files, the data will be automatically restored on next startup.</loc>
        </p>

        @if (Model.LastExportInfo != null)
        {
            <div class="export-status" style="background: var(--background); border-radius: 8px; padding: 0.75rem 1rem; margin-bottom: 1rem; font-size: 0.875rem;">
                <strong><loc key="Owner_Backup_LastExport">Last export</loc>:</strong>
                @Model.LastExportInfo.ExportedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                — @Model.LastExportInfo.AvatarCount <loc key="Owner_Backup_Avatars">avatars</loc>,
                @($"{Model.LastExportInfo.Database.SizeBytes / 1024}") KB <loc key="Owner_Backup_Database">database</loc>
            </div>
        }

        <form method="post" asp-page-handler="PrepareForUpdate">
            <button type="submit" class="btn btn-primary"
                    onclick="return confirm('This will create a deployment export bundle. The bundle contains database credentials and should be treated as sensitive material. Proceed?')">
                &#x1F4E6; <loc key="Owner_Backup_ExportNow">Export Now</loc>
            </button>
        </form>
    </div>
```

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build ShiftManager.csproj --no-restore 2>&1 | tail -5`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add Pages/Owner/Backup.cshtml.cs Pages/Owner/Backup.cshtml
git commit -m "feat: add 'Prepare for Update' export UI to Owner Backup page"
```

---

### Task 7: Run full test suite and verify end-to-end

**Files:** None (verification only)

- [ ] **Step 1: Run the full test suite**

Run: `dotnet test ShiftManager.Tests -v normal 2>&1 | tail -20`
Expected: All tests pass, including the new DeploymentExportService tests.

- [ ] **Step 2: Verify the build succeeds cleanly**

Run: `dotnet build ShiftManager.csproj --no-restore 2>&1 | tail -5`
Expected: `Build succeeded.` with 0 warnings related to the new code.

- [ ] **Step 3: Verify the export service tests specifically**

Run: `dotnet test ShiftManager.Tests --filter "FullyQualifiedName~DeploymentExportService" -v normal 2>&1`
Expected: All Phase 1, Export, and Restore tests pass.

- [ ] **Step 4: Final commit with all files**

Verify nothing is unstaged:

```bash
git status
```

If any new files remain unstaged, add and commit them. Then create a summary commit if needed.

---

## Task Dependency Graph

```
Task 1 (Interface + Model)
  └── Task 2 (Phase 1 RestoreConfigAndKeys)
       └── Task 3 (ExportAsync + GetLastExportInfo)
            └── Task 4 (Phase 2 RestoreDataAsync)
                 └── Task 5 (Program.cs wiring)
                      └── Task 6 (Backup page UI)
                           └── Task 7 (Full verification)
```

All tasks are sequential — each builds on the previous.
