using System.Text.Json.Serialization;

namespace ShiftManager.Services;

public interface IDeploymentExportService
{
    Task<ExportResult> ExportAsync(int userId, string userEmail, string? exportPath = null);
    Task RestoreDataAsync(IServiceProvider serviceProvider);
    ExportManifest? GetLastExportInfo(string? exportPath = null);
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
