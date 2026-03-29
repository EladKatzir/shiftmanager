using FluentAssertions;
using System.Text.Json;
using ShiftManager.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

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
        DeploymentExportService.PendingDataRestore = false;
        DeploymentExportService.RestoreJustCompleted = false;
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
        // No appsettings.Production.json — File.Copy will fail internally
        var act = () => DeploymentExportService.RestoreConfigAndKeys(_exportDir, _appBaseDir);
        act.Should().NotThrow();
    }
}

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
        DeploymentExportService.PendingDataRestore = false;
        DeploymentExportService.RestoreJustCompleted = false;
        try { Directory.Delete(_testDir, recursive: true); }
        catch { /* cleanup best-effort */ }
    }

    private DeploymentExportService CreateService()
    {
        var envMock = new Mock<IWebHostEnvironment>();
        envMock.Setup(e => e.WebRootPath).Returns(_webRootPath);
        envMock.Setup(e => e.ContentRootPath).Returns(_contentRootPath);

        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c["ConnectionStrings:Default"])
            .Returns($"Data Source={Path.Combine(_testDir, "data", "app.db")}");

        var loggerMock = Mock.Of<ILogger<DeploymentExportService>>();
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();

        return new DeploymentExportService(envMock.Object, configMock.Object, loggerMock, scopeFactoryMock.Object);
    }

    [Fact]
    public async Task ExportAsync_MissingProductionConfig_ReturnsError()
    {
        var service = CreateService();
        var result = await service.ExportAsync(1, "test@test.com", _exportDir);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("appsettings.Production.json");
    }

    [Fact]
    public async Task ExportAsync_CreatesManifestWithCorrectCounts()
    {
        var service = CreateService();
        File.WriteAllText(Path.Combine(_contentRootPath, "appsettings.Production.json"), "{}");

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

        var avatarDir = Path.Combine(_webRootPath, "avatars", "1");
        Directory.CreateDirectory(avatarDir);
        File.WriteAllText(Path.Combine(avatarDir, "5.jpg"), "fake-avatar");
        File.WriteAllText(Path.Combine(avatarDir, "5_thumb.jpg"), "fake-thumb");

        var result = await service.ExportAsync(1, "test@test.com", _exportDir);

        result.Success.Should().BeTrue(because: $"ErrorMessage was: {result.ErrorMessage}");
        result.Manifest.Should().NotBeNull();
        result.Manifest!.AvatarCount.Should().Be(2);
        result.Manifest.ConfigIncluded.Should().BeTrue();
        result.Manifest.MachineName.Should().Be(Environment.MachineName);
        File.Exists(Path.Combine(_exportDir, "manifest.json")).Should().BeTrue();
        File.Exists(Path.Combine(_exportDir, "app.db")).Should().BeTrue();
    }

    [Fact]
    public async Task ExportAsync_AtomicReplace_PreservesPreviousOnFailure()
    {
        var service = CreateService();
        Directory.CreateDirectory(_exportDir);
        File.WriteAllText(Path.Combine(_exportDir, "previous-marker.txt"), "old");
        File.WriteAllText(Path.Combine(_exportDir, "manifest.json"), "{}");

        var result = await service.ExportAsync(1, "test@test.com", _exportDir);

        result.Success.Should().BeFalse();
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

    [Fact]
    public async Task ExportAsync_ConcurrentCall_ReturnsError()
    {
        var service = CreateService();
        File.WriteAllText(Path.Combine(_contentRootPath, "appsettings.Production.json"), "{}");

        var lockField = typeof(DeploymentExportService)
            .GetField("_exportLock", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var semaphore = (SemaphoreSlim)lockField.GetValue(null)!;
        await semaphore.WaitAsync();
        try
        {
            var result = await service.ExportAsync(1, "test@test.com", _exportDir);
            result.Success.Should().BeFalse();
            result.ErrorMessage.Should().Contain("already in progress");
        }
        finally
        {
            semaphore.Release();
        }
    }
}
