namespace ShiftManager.Services;

/// <summary>
/// Service for purging historical data with safety mechanisms
/// </summary>
public interface IPurgeService
{
    /// <summary>
    /// Purge historical data with safety checks
    /// </summary>
    Task<PurgeResult> PurgeDataAsync(PurgeRequest request);

    /// <summary>
    /// Validate typed confirmation matches expected format
    /// </summary>
    bool ValidateConfirmation(string confirmation, string companyName, DateOnly cutoffDate);
}

/// <summary>
/// Request to purge historical data
/// </summary>
public class PurgeRequest
{
    public DateOnly CutoffDate { get; set; }
    public ArchiveDataTypes Types { get; set; }
    public string TypedConfirmation { get; set; } = string.Empty;
    public bool ArchiveConfirmed { get; set; }
    public bool RunVacuum { get; set; }
    public bool HardDeleteOnDuty { get; set; } // Owner-only checkbox
}

/// <summary>
/// Result of purge operation
/// </summary>
public class PurgeResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string BackupPath { get; set; } = string.Empty;
    public Dictionary<string, int> DeletedCounts { get; set; } = new();
    public long DbSizeBeforeBytes { get; set; }
    public long DbSizeAfterBytes { get; set; }
    public TimeSpan Duration { get; set; }
}
