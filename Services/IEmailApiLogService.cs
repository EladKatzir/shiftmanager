using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Service for managing email API diagnostic logs.
/// Used for troubleshooting email integration issues in air-gapped environments.
/// </summary>
public interface IEmailApiLogService
{
    /// <summary>
    /// Logs an email API call with full request/response details.
    /// </summary>
    /// <param name="requestUrl">The API endpoint URL</param>
    /// <param name="requestMethod">HTTP method (usually POST)</param>
    /// <param name="requestHeaders">HTTP request headers</param>
    /// <param name="requestBody">HTTP request body (email payload)</param>
    /// <param name="responseStatusCode">HTTP response status code (null if network error)</param>
    /// <param name="responseHeaders">HTTP response headers</param>
    /// <param name="responseBody">HTTP response body</param>
    /// <param name="recipientEmail">Email recipient address</param>
    /// <param name="emailSubject">Email subject line</param>
    /// <param name="success">Whether the email was sent successfully</param>
    /// <param name="errorMessage">Error message if failed</param>
    /// <param name="durationMs">Request duration in milliseconds</param>
    /// <param name="validationErrors">List of validation errors (if any)</param>
    /// <returns>The created log entry</returns>
    /// <param name="companyId">Optional CompanyId to set on the log entity before saving.
    /// When provided (non-zero), bypasses CompanyIdInterceptor resolution, enabling persistence
    /// from background services that lack HTTP tenant context.</param>
    Task<EmailApiLog> LogEmailApiCallAsync(
        string requestUrl,
        string requestMethod,
        Dictionary<string, string> requestHeaders,
        string requestBody,
        int? responseStatusCode,
        Dictionary<string, string>? responseHeaders,
        string? responseBody,
        string recipientEmail,
        string emailSubject,
        bool success,
        string? errorMessage,
        int durationMs,
        List<string>? validationErrors = null,
        int companyId = 0);

    /// <summary>
    /// Gets the most recent email API logs for the current company.
    /// </summary>
    /// <param name="count">Number of logs to retrieve (default: 100)</param>
    /// <returns>List of recent logs, ordered by timestamp descending</returns>
    Task<List<EmailApiLog>> GetRecentLogsAsync(int count = 100);

    /// <summary>
    /// Gets recent failed email attempts for the current company.
    /// </summary>
    /// <param name="count">Number of failed logs to retrieve (default: 50)</param>
    /// <returns>List of failed logs, ordered by timestamp descending</returns>
    Task<List<EmailApiLog>> GetFailedLogsAsync(int count = 50);

    /// <summary>
    /// Gets a specific log entry by ID.
    /// </summary>
    /// <param name="id">The log entry ID</param>
    /// <returns>The log entry, or null if not found</returns>
    Task<EmailApiLog?> GetLogByIdAsync(int id);

    /// <summary>
    /// Removes old log entries to keep database size manageable.
    /// </summary>
    /// <param name="retentionDays">Number of days to keep logs (default: 90)</param>
    /// <returns>Number of log entries deleted</returns>
    Task<int> CleanupOldLogsAsync(int retentionDays = 90);
}
