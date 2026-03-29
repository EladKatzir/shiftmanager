using FluentAssertions;
using System.Text.Json;
using ShiftManager.Services;

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
