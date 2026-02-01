using ShiftManager.Models.Telemetry;

namespace ShiftManager.Services;

/// <summary>
/// Service for managing client-side telemetry data in air-gapped environments.
/// Provides local observability for analytics events, errors, and performance metrics.
/// B-019, B-020, B-021: Local Observability Stack
/// </summary>
public interface IClientTelemetryService
{
    // ========================================
    // Analytics Events (B-019)
    // ========================================

    /// <summary>
    /// Logs an analytics event from the client.
    /// </summary>
    /// <param name="evt">The analytics event to log</param>
    /// <returns>The created event (with ID populated)</returns>
    Task<ClientAnalyticsEvent> LogEventAsync(ClientAnalyticsEvent evt);

    /// <summary>
    /// Logs a batch of analytics events from the client.
    /// </summary>
    /// <param name="events">The events to log</param>
    /// <returns>Number of events successfully logged</returns>
    Task<int> LogEventBatchAsync(IEnumerable<ClientAnalyticsEvent> events);

    /// <summary>
    /// Gets recent analytics events.
    /// </summary>
    /// <param name="count">Number of events to retrieve (default: 100)</param>
    /// <param name="eventType">Optional filter by event type</param>
    /// <returns>List of recent events, ordered by timestamp descending</returns>
    Task<List<ClientAnalyticsEvent>> GetRecentEventsAsync(int count = 100, string? eventType = null);

    /// <summary>
    /// Gets event counts grouped by type for a date range.
    /// </summary>
    /// <param name="startDate">Start of date range (UTC)</param>
    /// <param name="endDate">End of date range (UTC)</param>
    /// <returns>Dictionary of event type to count</returns>
    Task<Dictionary<string, int>> GetEventCountsByTypeAsync(DateTime startDate, DateTime endDate);

    // ========================================
    // Client Errors (B-020)
    // ========================================

    /// <summary>
    /// Logs a client-side error.
    /// </summary>
    /// <param name="error">The error to log</param>
    /// <returns>The created error (with ID populated)</returns>
    Task<ClientError> LogErrorAsync(ClientError error);

    /// <summary>
    /// Logs a batch of client-side errors.
    /// </summary>
    /// <param name="errors">The errors to log</param>
    /// <returns>Number of errors successfully logged</returns>
    Task<int> LogErrorBatchAsync(IEnumerable<ClientError> errors);

    /// <summary>
    /// Gets recent client errors.
    /// </summary>
    /// <param name="count">Number of errors to retrieve (default: 100)</param>
    /// <returns>List of recent errors, ordered by timestamp descending</returns>
    Task<List<ClientError>> GetRecentErrorsAsync(int count = 100);

    /// <summary>
    /// Gets error counts grouped by type for a date range.
    /// </summary>
    /// <param name="startDate">Start of date range (UTC)</param>
    /// <param name="endDate">End of date range (UTC)</param>
    /// <returns>Dictionary of error type to count</returns>
    Task<Dictionary<string, int>> GetErrorCountsByTypeAsync(DateTime startDate, DateTime endDate);

    // ========================================
    // Performance Metrics (B-021)
    // ========================================

    /// <summary>
    /// Logs a performance metric.
    /// </summary>
    /// <param name="metric">The metric to log</param>
    /// <returns>The created metric (with ID populated)</returns>
    Task<PerformanceMetric> LogPerformanceAsync(PerformanceMetric metric);

    /// <summary>
    /// Logs a batch of performance metrics.
    /// </summary>
    /// <param name="metrics">The metrics to log</param>
    /// <returns>Number of metrics successfully logged</returns>
    Task<int> LogPerformanceBatchAsync(IEnumerable<PerformanceMetric> metrics);

    /// <summary>
    /// Gets recent performance metrics.
    /// </summary>
    /// <param name="count">Number of metrics to retrieve (default: 100)</param>
    /// <param name="metricName">Optional filter by metric name (LCP, FID, CLS, TTFB)</param>
    /// <returns>List of recent metrics, ordered by timestamp descending</returns>
    Task<List<PerformanceMetric>> GetRecentPerformanceAsync(int count = 100, string? metricName = null);

    /// <summary>
    /// Gets Core Web Vitals summary statistics for a date range.
    /// </summary>
    /// <param name="startDate">Start of date range (UTC)</param>
    /// <param name="endDate">End of date range (UTC)</param>
    /// <returns>Summary statistics per metric</returns>
    Task<Dictionary<string, WebVitalsSummary>> GetWebVitalsSummaryAsync(DateTime startDate, DateTime endDate);

    /// <summary>
    /// Gets Core Web Vitals percentiles (p50, p75, p95) per page.
    /// </summary>
    /// <param name="metricName">The metric name (LCP, FID, CLS, TTFB)</param>
    /// <param name="startDate">Start of date range (UTC)</param>
    /// <param name="endDate">End of date range (UTC)</param>
    /// <returns>Dictionary of page URL to percentile values</returns>
    Task<Dictionary<string, WebVitalsPercentiles>> GetWebVitalsPercentilesByPageAsync(
        string metricName, DateTime startDate, DateTime endDate);

    // ========================================
    // Data Retention
    // ========================================

    /// <summary>
    /// Removes old telemetry data to keep database size manageable.
    /// </summary>
    /// <param name="retentionDays">Number of days to keep data (default: 30)</param>
    /// <returns>Tuple of (events deleted, errors deleted, metrics deleted)</returns>
    Task<(int Events, int Errors, int Metrics)> CleanupOldDataAsync(int retentionDays = 30);
}

/// <summary>
/// Summary statistics for a Core Web Vital metric.
/// </summary>
public class WebVitalsSummary
{
    public string MetricName { get; set; } = string.Empty;
    public int SampleCount { get; set; }
    public double Average { get; set; }
    public double Median { get; set; }
    public double P75 { get; set; }
    public double P95 { get; set; }
    public int GoodCount { get; set; }
    public int NeedsImprovementCount { get; set; }
    public int PoorCount { get; set; }
}

/// <summary>
/// Percentile values for a Core Web Vital metric.
/// </summary>
public class WebVitalsPercentiles
{
    public double P50 { get; set; }
    public double P75 { get; set; }
    public double P95 { get; set; }
    public int SampleCount { get; set; }
}
