using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Models.Telemetry;
using ShiftManager.Services;

namespace ShiftManager.Pages.Api;

/// <summary>
/// API endpoint for client-side telemetry collection.
/// Receives analytics events, errors, and performance metrics from JavaScript.
/// B-019, B-020, B-021: Local Observability Stack (Air-Gapped)
/// </summary>
[AllowAnonymous] // Allow telemetry from unauthenticated pages (login, etc.)
[IgnoreAntiforgeryToken] // Client-side telemetry needs to work without CSRF tokens
public class TelemetryModel : PageModel
{
    private readonly IClientTelemetryService _telemetryService;
    private readonly ILogger<TelemetryModel> _logger;

    // Rate limiting: max requests per minute per IP
    // ConcurrentDictionary for thread-safe key access; lock still needed for Queue operations
    private static readonly ConcurrentDictionary<string, Queue<DateTime>> _rateLimitTracker = new();
    private static readonly object _rateLimitLock = new();
    private const int MaxRequestsPerMinute = 30;
    private const int MaxTrackedIps = 1000;
    private static DateTime _lastCleanup = DateTime.UtcNow;

    // Telemetry is intentionally tolerant: it must accept any client that can serialize JSON,
    // including clients whose Content-Type header was stripped by a proxy, mobile carrier,
    // browser extension, or legacy fetch wrapper. Dropping observability data because a
    // header is missing would defeat the feature's purpose (capturing bad-state-of-the-world).
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public TelemetryModel(IClientTelemetryService telemetryService, ILogger<TelemetryModel> logger)
    {
        _telemetryService = telemetryService;
        _logger = logger;
    }

    /// <summary>
    /// POST /Api/Telemetry?handler=Event
    /// Logs a single analytics event.
    /// </summary>
    public async Task<IActionResult> OnPostEventAsync()
    {
        if (!IsRateLimitAllowed())
        {
            return new JsonResult(new { error = "Rate limit exceeded" }) { StatusCode = 429 };
        }

        var dto = await TryReadJsonBodyAsync<AnalyticsEventDto>();
        if (dto == null || string.IsNullOrEmpty(dto.EventType))
        {
            return BadRequest(new { error = "Event type is required" });
        }

        // D-08: Validate and truncate field lengths to prevent data stuffing
        dto.EventType = TruncateField(dto.EventType, 100)!;
        dto.EventData = TruncateField(dto.EventData, 2000);
        dto.PageUrl = TruncateField(dto.PageUrl, 500);
        dto.SessionId = TruncateField(dto.SessionId, 100);

        var evt = new ClientAnalyticsEvent
        {
            EventType = dto.EventType,
            EventData = dto.EventData,
            UserIdHash = GetUserIdHash(),
            SessionId = dto.SessionId,
            PageUrl = dto.PageUrl ?? string.Empty,
            UserAgent = Request.Headers.UserAgent.ToString(),
            Timestamp = dto.Timestamp ?? DateTime.UtcNow
        };

        await _telemetryService.LogEventAsync(evt);

        return new JsonResult(new { success = true, id = evt.Id });
    }

    /// <summary>
    /// POST /Api/Telemetry?handler=EventBatch
    /// Logs a batch of analytics events.
    /// </summary>
    public async Task<IActionResult> OnPostEventBatchAsync()
    {
        if (!IsRateLimitAllowed())
        {
            return new JsonResult(new { error = "Rate limit exceeded" }) { StatusCode = 429 };
        }

        var dtos = await TryReadJsonBodyAsync<List<AnalyticsEventDto>>();
        if (dtos == null || !dtos.Any())
        {
            return BadRequest(new { error = "Events array is required" });
        }

        // Limit batch size
        if (dtos.Count > 50)
        {
            dtos = dtos.Take(50).ToList();
        }

        var userIdHash = GetUserIdHash();
        var userAgent = Request.Headers.UserAgent.ToString();

        var events = dtos.Select(dto => new ClientAnalyticsEvent
        {
            EventType = TruncateField(dto.EventType, 100) ?? string.Empty,
            EventData = TruncateField(dto.EventData, 2000),
            UserIdHash = userIdHash,
            SessionId = TruncateField(dto.SessionId, 100),
            PageUrl = TruncateField(dto.PageUrl, 500) ?? string.Empty,
            UserAgent = userAgent,
            Timestamp = dto.Timestamp ?? DateTime.UtcNow
        }).ToList();

        var count = await _telemetryService.LogEventBatchAsync(events);

        return new JsonResult(new { success = true, count });
    }

    /// <summary>
    /// POST /Api/Telemetry?handler=Error
    /// Logs a single client-side error.
    /// </summary>
    public async Task<IActionResult> OnPostErrorAsync()
    {
        if (!IsRateLimitAllowed())
        {
            return new JsonResult(new { error = "Rate limit exceeded" }) { StatusCode = 429 };
        }

        var dto = await TryReadJsonBodyAsync<ClientErrorDto>();
        if (dto == null || string.IsNullOrEmpty(dto.Message))
        {
            return BadRequest(new { error = "Error message is required" });
        }

        // D-08: Validate and truncate field lengths
        dto.Message = TruncateField(dto.Message, 1000)!;
        dto.StackTrace = TruncateField(dto.StackTrace, 5000);
        dto.Source = TruncateField(dto.Source, 500);
        dto.PageUrl = TruncateField(dto.PageUrl, 500);
        dto.ErrorType = TruncateField(dto.ErrorType, 100);

        var error = new ClientError
        {
            Message = dto.Message,
            StackTrace = dto.StackTrace,
            Source = dto.Source,
            LineNumber = dto.LineNumber,
            ColumnNumber = dto.ColumnNumber,
            ErrorType = dto.ErrorType,
            PageUrl = dto.PageUrl ?? string.Empty,
            UserAgent = Request.Headers.UserAgent.ToString(),
            UserIdHash = GetUserIdHash(),
            Timestamp = dto.Timestamp ?? DateTime.UtcNow,
            BrowserInfo = ParseBrowserInfo(Request.Headers.UserAgent.ToString())
        };

        await _telemetryService.LogErrorAsync(error);

        return new JsonResult(new { success = true, id = error.Id });
    }

    /// <summary>
    /// POST /Api/Telemetry?handler=ErrorBatch
    /// Logs a batch of client-side errors.
    /// </summary>
    public async Task<IActionResult> OnPostErrorBatchAsync()
    {
        if (!IsRateLimitAllowed())
        {
            return new JsonResult(new { error = "Rate limit exceeded" }) { StatusCode = 429 };
        }

        var dtos = await TryReadJsonBodyAsync<List<ClientErrorDto>>();
        if (dtos == null || !dtos.Any())
        {
            return BadRequest(new { error = "Errors array is required" });
        }

        // Limit batch size
        if (dtos.Count > 20)
        {
            dtos = dtos.Take(20).ToList();
        }

        var userIdHash = GetUserIdHash();
        var userAgent = Request.Headers.UserAgent.ToString();
        var browserInfo = ParseBrowserInfo(userAgent);

        var errors = dtos.Select(dto => new ClientError
        {
            Message = TruncateField(dto.Message, 1000) ?? string.Empty,
            StackTrace = TruncateField(dto.StackTrace, 5000),
            Source = TruncateField(dto.Source, 500),
            LineNumber = dto.LineNumber,
            ColumnNumber = dto.ColumnNumber,
            ErrorType = TruncateField(dto.ErrorType, 100),
            PageUrl = TruncateField(dto.PageUrl, 500) ?? string.Empty,
            UserAgent = userAgent,
            UserIdHash = userIdHash,
            Timestamp = dto.Timestamp ?? DateTime.UtcNow,
            BrowserInfo = browserInfo
        }).ToList();

        var count = await _telemetryService.LogErrorBatchAsync(errors);

        return new JsonResult(new { success = true, count });
    }

    /// <summary>
    /// POST /Api/Telemetry?handler=Performance
    /// Logs a single performance metric.
    /// </summary>
    public async Task<IActionResult> OnPostPerformanceAsync()
    {
        if (!IsRateLimitAllowed())
        {
            return new JsonResult(new { error = "Rate limit exceeded" }) { StatusCode = 429 };
        }

        var dto = await TryReadJsonBodyAsync<PerformanceMetricDto>();
        if (dto == null || string.IsNullOrEmpty(dto.MetricName))
        {
            return BadRequest(new { error = "Metric name is required" });
        }

        // D-08: Validate and truncate field lengths
        dto.MetricName = TruncateField(dto.MetricName, 100)!;
        dto.PageUrl = TruncateField(dto.PageUrl, 500);
        dto.Rating = TruncateField(dto.Rating, 50);

        var metric = new PerformanceMetric
        {
            MetricName = dto.MetricName,
            Value = dto.Value,
            Rating = dto.Rating,
            PageUrl = dto.PageUrl ?? string.Empty,
            UserAgent = Request.Headers.UserAgent.ToString(),
            ConnectionType = dto.ConnectionType,
            EffectiveType = dto.EffectiveType,
            DeviceMemory = dto.DeviceMemory,
            HardwareConcurrency = dto.HardwareConcurrency,
            Timestamp = dto.Timestamp ?? DateTime.UtcNow,
            BrowserInfo = ParseBrowserInfo(Request.Headers.UserAgent.ToString())
        };

        await _telemetryService.LogPerformanceAsync(metric);

        return new JsonResult(new { success = true, id = metric.Id });
    }

    /// <summary>
    /// POST /Api/Telemetry?handler=PerformanceBatch
    /// Logs a batch of performance metrics.
    /// </summary>
    public async Task<IActionResult> OnPostPerformanceBatchAsync()
    {
        if (!IsRateLimitAllowed())
        {
            return new JsonResult(new { error = "Rate limit exceeded" }) { StatusCode = 429 };
        }

        var dtos = await TryReadJsonBodyAsync<List<PerformanceMetricDto>>();
        if (dtos == null || !dtos.Any())
        {
            return BadRequest(new { error = "Metrics array is required" });
        }

        // Limit batch size
        if (dtos.Count > 50)
        {
            dtos = dtos.Take(50).ToList();
        }

        var userAgent = Request.Headers.UserAgent.ToString();
        var browserInfo = ParseBrowserInfo(userAgent);

        var metrics = dtos.Select(dto => new PerformanceMetric
        {
            MetricName = dto.MetricName ?? string.Empty,
            Value = dto.Value,
            Rating = dto.Rating,
            PageUrl = dto.PageUrl ?? string.Empty,
            UserAgent = userAgent,
            ConnectionType = dto.ConnectionType,
            EffectiveType = dto.EffectiveType,
            DeviceMemory = dto.DeviceMemory,
            HardwareConcurrency = dto.HardwareConcurrency,
            Timestamp = dto.Timestamp ?? DateTime.UtcNow,
            BrowserInfo = browserInfo
        }).ToList();

        var count = await _telemetryService.LogPerformanceBatchAsync(metrics);

        return new JsonResult(new { success = true, count });
    }

    /// <summary>
    /// Gets a hashed user ID for the current request.
    /// Uses SHA256 to avoid storing PII.
    /// </summary>
    private string GetUserIdHash()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userIdClaim) && int.TryParse(userIdClaim, out var userId))
        {
            return ClientTelemetryService.HashUserId(userId);
        }

        // For anonymous users, hash the IP address
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        return ClientTelemetryService.HashUserId(ip.GetHashCode());
    }

    /// <summary>
    /// Simple rate limiting by IP address.
    /// </summary>
    private bool IsRateLimitAllowed()
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var now = DateTime.UtcNow;

        lock (_rateLimitLock)
        {
            // Periodic cleanup of stale entries to prevent unbounded growth
            if ((now - _lastCleanup).TotalMinutes > 5 || _rateLimitTracker.Count > MaxTrackedIps)
            {
                var staleKeys = _rateLimitTracker
                    .Where(kvp => kvp.Value.Count == 0 || (now - kvp.Value.Peek()).TotalMinutes > 2)
                    .Select(kvp => kvp.Key)
                    .ToList();
                foreach (var key in staleKeys)
                    _rateLimitTracker.TryRemove(key, out _);
                _lastCleanup = now;
            }

            var queue = _rateLimitTracker.GetOrAdd(ip, _ => new Queue<DateTime>());

            // Remove old entries (older than 1 minute)
            while (queue.Count > 0 && (now - queue.Peek()).TotalMinutes > 1)
            {
                queue.Dequeue();
            }

            if (queue.Count >= MaxRequestsPerMinute)
            {
                _logger.LogWarning("Telemetry rate limit exceeded for IP: {IP}", ip);
                return false;
            }

            queue.Enqueue(now);
            return true;
        }
    }

    /// <summary>
    /// Reads and deserializes the request body as JSON, regardless of the Content-Type header.
    /// Returns null on empty body, malformed JSON, or any read error — callers treat null
    /// the same as they previously treated a null [FromBody] parameter.
    /// </summary>
    private async Task<T?> TryReadJsonBodyAsync<T>() where T : class
    {
        try
        {
            using var reader = new StreamReader(Request.Body);
            var json = await reader.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonSerializer.Deserialize<T>(json, _jsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// D-08: Truncate field to maximum length to prevent data stuffing / storage abuse.
    /// </summary>
    private static string? TruncateField(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length > maxLength ? value[..maxLength] : value;
    }

    /// <summary>
    /// Parses browser information from user agent string.
    /// </summary>
    private static string ParseBrowserInfo(string userAgent)
    {
        if (string.IsNullOrEmpty(userAgent)) return "Unknown";

        // Simple parsing - could use a library for more accuracy
        if (userAgent.Contains("Firefox/"))
            return "Firefox";
        if (userAgent.Contains("Edg/"))
            return "Edge";
        if (userAgent.Contains("Chrome/"))
            return "Chrome";
        if (userAgent.Contains("Safari/") && !userAgent.Contains("Chrome"))
            return "Safari";
        if (userAgent.Contains("MSIE") || userAgent.Contains("Trident/"))
            return "Internet Explorer";

        return "Other";
    }
}

// ========================================
// DTOs for JSON binding
// ========================================

public class AnalyticsEventDto
{
    public string? EventType { get; set; }
    public string? EventData { get; set; }
    public string? SessionId { get; set; }
    public string? PageUrl { get; set; }
    public DateTime? Timestamp { get; set; }
}

public class ClientErrorDto
{
    public string? Message { get; set; }
    public string? StackTrace { get; set; }
    public string? Source { get; set; }
    public int? LineNumber { get; set; }
    public int? ColumnNumber { get; set; }
    public string? ErrorType { get; set; }
    public string? PageUrl { get; set; }
    public DateTime? Timestamp { get; set; }
}

public class PerformanceMetricDto
{
    public string? MetricName { get; set; }
    public double Value { get; set; }
    public string? Rating { get; set; }
    public string? PageUrl { get; set; }
    public string? ConnectionType { get; set; }
    public string? EffectiveType { get; set; }
    public double? DeviceMemory { get; set; }
    public int? HardwareConcurrency { get; set; }
    public DateTime? Timestamp { get; set; }
}
