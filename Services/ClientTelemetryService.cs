using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models.Telemetry;

namespace ShiftManager.Services;

/// <summary>
/// Service for managing client-side telemetry data in air-gapped environments.
/// Provides defensive logging - never throws exceptions to avoid disrupting the application.
/// B-019, B-020, B-021: Local Observability Stack
/// </summary>
public class ClientTelemetryService : IClientTelemetryService
{
    private readonly AppDbContext _context;
    private readonly ILogger<ClientTelemetryService> _logger;

    public ClientTelemetryService(AppDbContext context, ILogger<ClientTelemetryService> logger)
    {
        _context = context;
        _logger = logger;
    }

    // ========================================
    // Analytics Events (B-019)
    // ========================================

    public async Task<ClientAnalyticsEvent> LogEventAsync(ClientAnalyticsEvent evt)
    {
        try
        {
            // Ensure timestamp is set
            if (evt.Timestamp == default)
            {
                evt.Timestamp = DateTime.UtcNow;
            }

            // Scrub any PII from page URL
            evt.PageUrl = ScrubPiiFromUrl(evt.PageUrl);

            _context.ClientAnalyticsEvents.Add(evt);
            await _context.SaveChangesAsync();

            _logger.LogDebug("Analytics event logged: {EventType} on {PageUrl}", evt.EventType, evt.PageUrl);

            return evt;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log analytics event: {EventType}", evt.EventType);
            return evt; // Return the event even if not persisted
        }
    }

    public async Task<int> LogEventBatchAsync(IEnumerable<ClientAnalyticsEvent> events)
    {
        try
        {
            var eventList = events.ToList();
            if (!eventList.Any()) return 0;

            foreach (var evt in eventList)
            {
                if (evt.Timestamp == default)
                {
                    evt.Timestamp = DateTime.UtcNow;
                }
                evt.PageUrl = ScrubPiiFromUrl(evt.PageUrl);
            }

            _context.ClientAnalyticsEvents.AddRange(eventList);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Logged batch of {Count} analytics events", eventList.Count);

            return eventList.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log analytics event batch");
            return 0;
        }
    }

    public async Task<List<ClientAnalyticsEvent>> GetRecentEventsAsync(int count = 100, string? eventType = null)
    {
        try
        {
            var query = _context.ClientAnalyticsEvents.AsQueryable();

            if (!string.IsNullOrEmpty(eventType))
            {
                query = query.Where(e => e.EventType == eventType);
            }

            return await query
                .OrderByDescending(e => e.Timestamp)
                .Take(count)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve recent analytics events");
            return new List<ClientAnalyticsEvent>();
        }
    }

    public async Task<Dictionary<string, int>> GetEventCountsByTypeAsync(DateTime startDate, DateTime endDate)
    {
        try
        {
            return await _context.ClientAnalyticsEvents
                .Where(e => e.Timestamp >= startDate && e.Timestamp <= endDate)
                .GroupBy(e => e.EventType)
                .Select(g => new { EventType = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.EventType, x => x.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get event counts by type");
            return new Dictionary<string, int>();
        }
    }

    // ========================================
    // Client Errors (B-020)
    // ========================================

    public async Task<ClientError> LogErrorAsync(ClientError error)
    {
        try
        {
            if (error.Timestamp == default)
            {
                error.Timestamp = DateTime.UtcNow;
            }

            // Scrub PII from error data
            error.Message = ScrubPiiFromMessage(error.Message);
            error.PageUrl = ScrubPiiFromUrl(error.PageUrl);
            if (error.StackTrace != null)
            {
                error.StackTrace = ScrubPiiFromMessage(error.StackTrace);
            }

            _context.ClientErrors.Add(error);
            await _context.SaveChangesAsync();

            _logger.LogWarning("Client error logged: {ErrorType} - {Message} on {PageUrl}",
                error.ErrorType ?? "Unknown", TruncateForLog(error.Message), error.PageUrl);

            return error;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log client error");
            return error;
        }
    }

    public async Task<int> LogErrorBatchAsync(IEnumerable<ClientError> errors)
    {
        try
        {
            var errorList = errors.ToList();
            if (!errorList.Any()) return 0;

            foreach (var error in errorList)
            {
                if (error.Timestamp == default)
                {
                    error.Timestamp = DateTime.UtcNow;
                }
                error.Message = ScrubPiiFromMessage(error.Message);
                error.PageUrl = ScrubPiiFromUrl(error.PageUrl);
                if (error.StackTrace != null)
                {
                    error.StackTrace = ScrubPiiFromMessage(error.StackTrace);
                }
            }

            _context.ClientErrors.AddRange(errorList);
            await _context.SaveChangesAsync();

            _logger.LogWarning("Logged batch of {Count} client errors", errorList.Count);

            return errorList.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log client error batch");
            return 0;
        }
    }

    public async Task<List<ClientError>> GetRecentErrorsAsync(int count = 100)
    {
        try
        {
            return await _context.ClientErrors
                .OrderByDescending(e => e.Timestamp)
                .Take(count)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve recent client errors");
            return new List<ClientError>();
        }
    }

    public async Task<Dictionary<string, int>> GetErrorCountsByTypeAsync(DateTime startDate, DateTime endDate)
    {
        try
        {
            return await _context.ClientErrors
                .Where(e => e.Timestamp >= startDate && e.Timestamp <= endDate)
                .GroupBy(e => e.ErrorType ?? "Unknown")
                .Select(g => new { ErrorType = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ErrorType, x => x.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get error counts by type");
            return new Dictionary<string, int>();
        }
    }

    // ========================================
    // Performance Metrics (B-021)
    // ========================================

    public async Task<PerformanceMetric> LogPerformanceAsync(PerformanceMetric metric)
    {
        try
        {
            if (metric.Timestamp == default)
            {
                metric.Timestamp = DateTime.UtcNow;
            }

            // Scrub PII from URL
            metric.PageUrl = ScrubPiiFromUrl(metric.PageUrl);

            // Calculate rating if not provided
            if (string.IsNullOrEmpty(metric.Rating))
            {
                metric.Rating = CalculateWebVitalRating(metric.MetricName, metric.Value);
            }

            _context.PerformanceMetrics.Add(metric);
            await _context.SaveChangesAsync();

            _logger.LogDebug("Performance metric logged: {MetricName}={Value}ms ({Rating}) on {PageUrl}",
                metric.MetricName, metric.Value, metric.Rating, metric.PageUrl);

            return metric;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log performance metric: {MetricName}", metric.MetricName);
            return metric;
        }
    }

    public async Task<int> LogPerformanceBatchAsync(IEnumerable<PerformanceMetric> metrics)
    {
        try
        {
            var metricList = metrics.ToList();
            if (!metricList.Any()) return 0;

            foreach (var metric in metricList)
            {
                if (metric.Timestamp == default)
                {
                    metric.Timestamp = DateTime.UtcNow;
                }
                metric.PageUrl = ScrubPiiFromUrl(metric.PageUrl);
                if (string.IsNullOrEmpty(metric.Rating))
                {
                    metric.Rating = CalculateWebVitalRating(metric.MetricName, metric.Value);
                }
            }

            _context.PerformanceMetrics.AddRange(metricList);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Logged batch of {Count} performance metrics", metricList.Count);

            return metricList.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log performance metric batch");
            return 0;
        }
    }

    public async Task<List<PerformanceMetric>> GetRecentPerformanceAsync(int count = 100, string? metricName = null)
    {
        try
        {
            var query = _context.PerformanceMetrics.AsQueryable();

            if (!string.IsNullOrEmpty(metricName))
            {
                query = query.Where(m => m.MetricName == metricName);
            }

            return await query
                .OrderByDescending(m => m.Timestamp)
                .Take(count)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve recent performance metrics");
            return new List<PerformanceMetric>();
        }
    }

    public async Task<Dictionary<string, WebVitalsSummary>> GetWebVitalsSummaryAsync(DateTime startDate, DateTime endDate)
    {
        try
        {
            var metrics = await _context.PerformanceMetrics
                .Where(m => m.Timestamp >= startDate && m.Timestamp <= endDate)
                .ToListAsync();

            var result = new Dictionary<string, WebVitalsSummary>();
            var metricNames = new[] { "LCP", "FID", "INP", "CLS", "TTFB" };

            foreach (var metricName in metricNames)
            {
                var values = metrics
                    .Where(m => m.MetricName == metricName)
                    .Select(m => m.Value)
                    .OrderBy(v => v)
                    .ToList();

                if (!values.Any()) continue;

                result[metricName] = new WebVitalsSummary
                {
                    MetricName = metricName,
                    SampleCount = values.Count,
                    Average = values.Average(),
                    Median = GetPercentile(values, 50),
                    P75 = GetPercentile(values, 75),
                    P95 = GetPercentile(values, 95),
                    GoodCount = metrics.Count(m => m.MetricName == metricName && m.Rating == "good"),
                    NeedsImprovementCount = metrics.Count(m => m.MetricName == metricName && m.Rating == "needs-improvement"),
                    PoorCount = metrics.Count(m => m.MetricName == metricName && m.Rating == "poor")
                };
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get web vitals summary");
            return new Dictionary<string, WebVitalsSummary>();
        }
    }

    public async Task<Dictionary<string, WebVitalsPercentiles>> GetWebVitalsPercentilesByPageAsync(
        string metricName, DateTime startDate, DateTime endDate)
    {
        try
        {
            var metrics = await _context.PerformanceMetrics
                .Where(m => m.MetricName == metricName && m.Timestamp >= startDate && m.Timestamp <= endDate)
                .ToListAsync();

            var result = new Dictionary<string, WebVitalsPercentiles>();

            var groupedByPage = metrics.GroupBy(m => m.PageUrl);

            foreach (var group in groupedByPage)
            {
                var values = group.Select(m => m.Value).OrderBy(v => v).ToList();
                if (!values.Any()) continue;

                result[group.Key] = new WebVitalsPercentiles
                {
                    P50 = GetPercentile(values, 50),
                    P75 = GetPercentile(values, 75),
                    P95 = GetPercentile(values, 95),
                    SampleCount = values.Count
                };
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get web vitals percentiles by page");
            return new Dictionary<string, WebVitalsPercentiles>();
        }
    }

    // ========================================
    // Data Retention
    // ========================================

    public async Task<(int Events, int Errors, int Metrics)> CleanupOldDataAsync(int retentionDays = 30)
    {
        try
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);
            var eventsDeleted = 0;
            var errorsDeleted = 0;
            var metricsDeleted = 0;

            // Delete old events
            var oldEvents = await _context.ClientAnalyticsEvents
                .Where(e => e.Timestamp < cutoffDate)
                .ToListAsync();
            if (oldEvents.Any())
            {
                _context.ClientAnalyticsEvents.RemoveRange(oldEvents);
                eventsDeleted = oldEvents.Count;
            }

            // Delete old errors
            var oldErrors = await _context.ClientErrors
                .Where(e => e.Timestamp < cutoffDate)
                .ToListAsync();
            if (oldErrors.Any())
            {
                _context.ClientErrors.RemoveRange(oldErrors);
                errorsDeleted = oldErrors.Count;
            }

            // Delete old metrics
            var oldMetrics = await _context.PerformanceMetrics
                .Where(m => m.Timestamp < cutoffDate)
                .ToListAsync();
            if (oldMetrics.Any())
            {
                _context.PerformanceMetrics.RemoveRange(oldMetrics);
                metricsDeleted = oldMetrics.Count;
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Telemetry cleanup complete: {Events} events, {Errors} errors, {Metrics} metrics deleted (older than {Days} days)",
                eventsDeleted, errorsDeleted, metricsDeleted, retentionDays);

            return (eventsDeleted, errorsDeleted, metricsDeleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup old telemetry data");
            return (0, 0, 0);
        }
    }

    // ========================================
    // Helper Methods
    // ========================================

    /// <summary>
    /// Hashes a user ID using SHA256 to remove PII.
    /// </summary>
    public static string HashUserId(int userId)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes($"shiftmgr-user-{userId}");
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Scrubs potential PII from URL query parameters.
    /// </summary>
    private static string ScrubPiiFromUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return string.Empty;

        // Remove query string to avoid PII in query params
        var queryIndex = url.IndexOf('?');
        if (queryIndex > 0)
        {
            url = url.Substring(0, queryIndex);
        }

        // Remove hash/fragment
        var hashIndex = url.IndexOf('#');
        if (hashIndex > 0)
        {
            url = url.Substring(0, hashIndex);
        }

        return url;
    }

    /// <summary>
    /// Scrubs potential PII patterns from error messages.
    /// </summary>
    private static string ScrubPiiFromMessage(string message)
    {
        if (string.IsNullOrEmpty(message)) return string.Empty;

        // Scrub email addresses
        message = System.Text.RegularExpressions.Regex.Replace(
            message,
            @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}",
            "[EMAIL]");

        // Scrub phone numbers (various formats)
        message = System.Text.RegularExpressions.Regex.Replace(
            message,
            @"(\+?\d{1,3}[-.\s]?)?\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}",
            "[PHONE]");

        // Scrub potential API keys or tokens (long alphanumeric strings)
        message = System.Text.RegularExpressions.Regex.Replace(
            message,
            @"[a-zA-Z0-9]{32,}",
            "[TOKEN]");

        return message;
    }

    /// <summary>
    /// Truncates a message for logging purposes.
    /// </summary>
    private static string TruncateForLog(string message, int maxLength = 100)
    {
        if (string.IsNullOrEmpty(message)) return string.Empty;
        return message.Length > maxLength ? message.Substring(0, maxLength) + "..." : message;
    }

    /// <summary>
    /// Calculates the Core Web Vitals rating based on thresholds.
    /// See: https://web.dev/vitals/
    /// </summary>
    private static string CalculateWebVitalRating(string metricName, double value)
    {
        return metricName.ToUpperInvariant() switch
        {
            "LCP" => value <= 2500 ? "good" : value <= 4000 ? "needs-improvement" : "poor",
            "FID" => value <= 100 ? "good" : value <= 300 ? "needs-improvement" : "poor",
            "INP" => value <= 200 ? "good" : value <= 500 ? "needs-improvement" : "poor",
            "CLS" => value <= 0.1 ? "good" : value <= 0.25 ? "needs-improvement" : "poor",
            "TTFB" => value <= 800 ? "good" : value <= 1800 ? "needs-improvement" : "poor",
            _ => "unknown"
        };
    }

    /// <summary>
    /// Calculates a percentile value from a sorted list.
    /// </summary>
    private static double GetPercentile(List<double> sortedValues, int percentile)
    {
        if (!sortedValues.Any()) return 0;
        if (sortedValues.Count == 1) return sortedValues[0];

        var index = (percentile / 100.0) * (sortedValues.Count - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);

        if (lower == upper) return sortedValues[lower];

        var weight = index - lower;
        return sortedValues[lower] * (1 - weight) + sortedValues[upper] * weight;
    }
}
