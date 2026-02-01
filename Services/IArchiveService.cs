namespace ShiftManager.Services;

/// <summary>
/// Service for creating and managing historical data archives
/// </summary>
public interface IArchiveService
{
    /// <summary>
    /// Preview counts for what would be archived
    /// </summary>
    Task<ArchivePreview> PreviewArchiveAsync(DateOnly cutoffDate, ArchiveDataTypes types);

    /// <summary>
    /// Create archive bundles (CSV + re-importable NDJSON) for historical data
    /// </summary>
    Task<ArchiveResult> CreateArchiveAsync(ArchiveRequest request);

    /// <summary>
    /// Get the most recent archive for the current company
    /// </summary>
    Task<ArchiveMetadata?> GetLatestArchiveAsync();

    /// <summary>
    /// Validate if an archive is "fresh" (matches exact cutoff/selection)
    /// </summary>
    Task<bool> ValidateFreshArchiveAsync(DateOnly cutoffDate, ArchiveDataTypes types);
}

/// <summary>
/// Request to create an archive
/// </summary>
public class ArchiveRequest
{
    public DateOnly CutoffDate { get; set; }
    public ArchiveDataTypes Types { get; set; }
}

/// <summary>
/// Types of data that can be archived
/// </summary>
[Flags]
public enum ArchiveDataTypes
{
    None = 0,
    Shifts = 1,           // ShiftInstance + ShiftAssignment
    SwapRequests = 2,
    TimeOff = 4,
    Chores = 8,
    OnDuty = 16,
    All = Shifts | SwapRequests | TimeOff | Chores | OnDuty
}

/// <summary>
/// Result of archive creation
/// </summary>
public class ArchiveResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }

    // CSV export
    public string CsvZipPath { get; set; } = string.Empty;
    public string CsvZipFileName { get; set; } = string.Empty;
    public long CsvZipSizeBytes { get; set; }

    // Re-importable export
    public string NdjsonZipPath { get; set; } = string.Empty;
    public string NdjsonZipFileName { get; set; } = string.Empty;
    public long NdjsonZipSizeBytes { get; set; }
    public string Sha256Hash { get; set; } = string.Empty;

    public ArchiveMetadata Metadata { get; set; } = new();
}

/// <summary>
/// Metadata about an archive (stored in audit log)
/// </summary>
public class ArchiveMetadata
{
    public int ArchiveId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public int CompanyId { get; set; }
    public DateOnly CutoffDate { get; set; }
    public ArchiveDataTypes Types { get; set; }
    public DateTime CreatedAt { get; set; }
    public int CreatedBy { get; set; }
    public Dictionary<string, int> RowCounts { get; set; } = new();
    public int SchemaVersion { get; set; } = 1;
}

/// <summary>
/// Preview of archive counts
/// </summary>
public class ArchivePreview
{
    public Dictionary<string, int> Counts { get; set; } = new();
    public DateOnly CutoffDate { get; set; }
    public long EstimatedSizeBytes { get; set; }
    public List<string> Warnings { get; set; } = new();
}
