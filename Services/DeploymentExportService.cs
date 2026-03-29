using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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

    // Phase 2 and ExportAsync — placeholder signatures to satisfy the interface.
    // Implemented in subsequent tasks.
    public Task<ExportResult> ExportAsync(int userId, string userEmail, string? exportPath = null)
        => throw new NotImplementedException("Implemented in Task 3");

    public Task RestoreDataAsync(IServiceProvider serviceProvider)
        => throw new NotImplementedException("Implemented in Task 4");

    public ExportManifest? GetLastExportInfo(string? exportPath = null)
        => throw new NotImplementedException("Implemented in Task 3");
}
