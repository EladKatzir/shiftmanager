using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Service for managing Griffin ADFS connection test diagnostic logs.
/// Used for troubleshooting Griffin authentication integration issues in air-gapped environments.
/// </summary>
public interface IGriffinApiLogService
{
    /// <summary>
    /// Logs a Griffin connection test attempt with full request/response details.
    /// </summary>
    /// <param name="requestUrl">The Griffin endpoint URL being tested</param>
    /// <param name="requestMethod">HTTP method (usually GET)</param>
    /// <param name="requestHeaders">HTTP request headers</param>
    /// <param name="responseStatusCode">HTTP response status code (null if network error)</param>
    /// <param name="responseHeaders">HTTP response headers</param>
    /// <param name="responseBody">HTTP response body</param>
    /// <param name="redirectUrl">URL where Griffin redirected (if applicable)</param>
    /// <param name="success">Whether the connection test succeeded</param>
    /// <param name="errorMessage">Error message if failed</param>
    /// <param name="durationMs">Request duration in milliseconds</param>
    /// <param name="validationErrors">List of validation errors (if any)</param>
    /// <returns>The created log entry</returns>
    Task<GriffinApiLog> LogConnectionTestAsync(
        string requestUrl,
        string requestMethod,
        Dictionary<string, string> requestHeaders,
        int? responseStatusCode,
        Dictionary<string, string>? responseHeaders,
        string? responseBody,
        string? redirectUrl,
        bool success,
        string? errorMessage,
        int durationMs,
        List<string>? validationErrors = null);

    /// <summary>
    /// Gets the most recent Griffin connection test logs for the current company.
    /// </summary>
    /// <param name="count">Number of logs to retrieve (default: 100)</param>
    /// <returns>List of recent logs, ordered by timestamp descending</returns>
    Task<List<GriffinApiLog>> GetRecentLogsAsync(int count = 100);

    /// <summary>
    /// Gets recent failed Griffin connection attempts for the current company.
    /// </summary>
    /// <param name="count">Number of failed logs to retrieve (default: 50)</param>
    /// <returns>List of failed logs, ordered by timestamp descending</returns>
    Task<List<GriffinApiLog>> GetFailedLogsAsync(int count = 50);

    /// <summary>
    /// Gets a specific log entry by ID.
    /// </summary>
    /// <param name="id">The log entry ID</param>
    /// <returns>The log entry, or null if not found</returns>
    Task<GriffinApiLog?> GetLogByIdAsync(int id);

    /// <summary>
    /// Removes old log entries to keep database size manageable.
    /// </summary>
    /// <param name="retentionDays">Number of days to keep logs (default: 90)</param>
    /// <returns>Number of log entries deleted</returns>
    Task<int> CleanupOldLogsAsync(int retentionDays = 90);
}
