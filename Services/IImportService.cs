namespace ShiftManager.Services;

/// <summary>
/// Service for importing archived data
/// </summary>
public interface IImportService
{
    /// <summary>
    /// Validate uploaded ZIP before import
    /// </summary>
    Task<ImportValidationResult> ValidateArchiveAsync(string zipPath);

    /// <summary>
    /// Import data from validated archive
    /// </summary>
    Task<ImportResult> ImportArchiveAsync(ImportRequest request);
}

/// <summary>
/// Request to import an archive
/// </summary>
public class ImportRequest
{
    public string ZipPath { get; set; } = string.Empty;
    public ImportConflictPolicy ConflictPolicy { get; set; } = ImportConflictPolicy.SkipDuplicates;
    public bool SkipMissingUsers { get; set; }
}

/// <summary>
/// Conflict resolution policy for import
/// </summary>
public enum ImportConflictPolicy
{
    SkipDuplicates,
    OverwriteExisting,
    FailOnConflict
}

/// <summary>
/// Result of archive validation
/// </summary>
public class ImportValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    // Validation checks
    public bool CompanyMatch { get; set; }
    public string ArchiveCompanyName { get; set; } = string.Empty;
    public string CurrentCompanyName { get; set; } = string.Empty;

    public List<string> MissingUsers { get; set; } = new(); // By email
    public List<string> MissingShiftTypeKeys { get; set; } = new();

    public ArchiveMetadata? Metadata { get; set; }
}

/// <summary>
/// Result of import operation
/// </summary>
public class ImportResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }

    public Dictionary<string, ImportEntityStats> Stats { get; set; } = new();
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Statistics for a single entity type during import
/// </summary>
public class ImportEntityStats
{
    public int Total { get; set; }
    public int Inserted { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
}

/// <summary>
/// NDJSON record structure
/// </summary>
public class NdjsonRecord
{
    public string Type { get; set; } = string.Empty;
    public object Data { get; set; } = new();
}
