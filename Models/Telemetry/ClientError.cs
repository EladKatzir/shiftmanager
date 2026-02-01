namespace ShiftManager.Models.Telemetry;

/// <summary>
/// Client-side JavaScript error log entry.
/// Captures unhandled errors and promise rejections.
/// B-020: Client-Side Error Tracking (LOCAL)
/// </summary>
public class ClientError
{
    /// <summary>
    /// Primary key
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Error message (PII scrubbed)
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// JavaScript stack trace
    /// </summary>
    public string? StackTrace { get; set; }

    /// <summary>
    /// Source file where the error occurred
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Line number in the source file
    /// </summary>
    public int? LineNumber { get; set; }

    /// <summary>
    /// Column number in the source file
    /// </summary>
    public int? ColumnNumber { get; set; }

    /// <summary>
    /// Error type (e.g., TypeError, ReferenceError, unhandledrejection)
    /// </summary>
    public string? ErrorType { get; set; }

    /// <summary>
    /// Page URL where the error occurred (path only, no query params with PII)
    /// </summary>
    public string PageUrl { get; set; } = string.Empty;

    /// <summary>
    /// Browser user agent string
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// SHA256 hash of user ID - no PII stored
    /// </summary>
    public string UserIdHash { get; set; } = string.Empty;

    /// <summary>
    /// When the error occurred (UTC)
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Browser information (parsed from user agent)
    /// </summary>
    public string? BrowserInfo { get; set; }
}
