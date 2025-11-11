namespace ShiftManager.Models.Api;

/// <summary>
/// Logs all API requests for observability and forensics.
/// Sidecar table for API monitoring.
/// </summary>
public class ApiRequestLog
{
    public int Id { get; set; }

    /// <summary>
    /// API key used for this request (nullable for unauthenticated attempts)
    /// </summary>
    public int? ApiKeyId { get; set; }

    /// <summary>
    /// Company ID from the API key
    /// </summary>
    public int? CompanyId { get; set; }

    /// <summary>
    /// HTTP method (GET, POST, etc.)
    /// </summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// Request path (e.g., /api/v1/users)
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Query string (optional)
    /// </summary>
    public string? QueryString { get; set; }

    /// <summary>
    /// HTTP status code returned
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    /// Request duration in milliseconds
    /// </summary>
    public int DurationMs { get; set; }

    /// <summary>
    /// Client IP address
    /// </summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>
    /// User agent string
    /// </summary>
    public string UserAgent { get; set; } = string.Empty;

    /// <summary>
    /// Correlation ID for request tracing
    /// </summary>
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>
    /// Error message if request failed (nullable)
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// When this request was made
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ApiKey? ApiKey { get; set; }
}
