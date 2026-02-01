using System.Diagnostics;

namespace ShiftManager.Middleware;

/// <summary>
/// Middleware for structured JSON logging of HTTP requests with performance metrics and correlation IDs.
/// Supports X-Request-ID header propagation for distributed tracing.
/// </summary>
/// <remarks>
/// B-022: Implements structured logging requirements:
/// - Logs request_id, user_id, endpoint, params, response_code, duration_ms
/// - Structured JSON format (via ILogger and JsonConsole provider)
/// - X-Request-ID header propagation from client
/// - Sensitive data redaction (passwords, tokens, secrets)
/// - Configurable log levels (DEBUG in dev, INFO in prod)
/// </remarks>
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    /// <summary>
    /// Keys that should be redacted from query strings and headers to prevent sensitive data leakage.
    /// </summary>
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "pwd", "pass", "passwd",
        "token", "access_token", "refresh_token", "bearer",
        "secret", "client_secret",
        "apikey", "api_key", "api-key",
        "authorization", "auth",
        "cookie", "session",
        "credential", "credentials",
        "key", "private_key", "privatekey"
    };

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // B-022: Accept X-Request-ID from client for correlation, or generate a new one
        var requestId = context.Request.Headers["X-Request-ID"].FirstOrDefault()
            ?? Guid.NewGuid().ToString("N")[..12];

        // Store for use by downstream middleware and handlers
        context.Items["RequestId"] = requestId;
        context.Items["CorrelationId"] = requestId; // Backward compatibility

        // Add to response headers for client correlation
        context.Response.Headers["X-Request-ID"] = requestId;

        var stopwatch = Stopwatch.StartNew();
        var request = context.Request;

        // Get user information if authenticated
        var userId = context.User?.FindFirst("sub")?.Value
            ?? context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? "anonymous";
        var ipAddress = GetClientIpAddress(context);
        var userAgent = context.Request.Headers.UserAgent.FirstOrDefault();

        // B-022: Log request start with structured data (DEBUG level)
        _logger.LogDebug(
            "HTTP request started: {Method} {Path} | RequestId={RequestId} UserId={UserId} IP={IpAddress}",
            request.Method, request.Path, requestId, userId, ipAddress);

        Exception? caughtException = null;

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            caughtException = ex;
            throw;
        }
        finally
        {
            stopwatch.Stop();
            LogRequestCompletion(context, requestId, userId, ipAddress, userAgent, stopwatch.ElapsedMilliseconds, caughtException);
        }
    }

    /// <summary>
    /// Logs the completed request with full structured data for JSON output.
    /// </summary>
    private void LogRequestCompletion(
        HttpContext context,
        string requestId,
        string userId,
        string ipAddress,
        string? userAgent,
        long durationMs,
        Exception? exception)
    {
        var statusCode = context.Response.StatusCode;
        var sanitizedQuery = SanitizeQueryString(context.Request.QueryString.Value);

        // B-022: Determine log level based on status code
        var logLevel = statusCode >= 500 ? LogLevel.Error
            : statusCode >= 400 ? LogLevel.Warning
            : LogLevel.Information;

        // B-022: Structured log with all required fields
        // Using named parameters enables structured JSON logging when JsonConsole is configured
        if (exception != null)
        {
            _logger.Log(logLevel, exception,
                "HTTP {Method} {Path} completed with {StatusCode} in {DurationMs}ms | RequestId={RequestId} UserId={UserId} Query={Query} IP={IpAddress} UserAgent={UserAgent} Error={ErrorType}",
                context.Request.Method,
                context.Request.Path.Value,
                statusCode,
                durationMs,
                requestId,
                userId,
                sanitizedQuery,
                ipAddress,
                userAgent ?? "unknown",
                exception.GetType().Name);
        }
        else
        {
            _logger.Log(logLevel,
                "HTTP {Method} {Path} completed with {StatusCode} in {DurationMs}ms | RequestId={RequestId} UserId={UserId} Query={Query} IP={IpAddress} UserAgent={UserAgent}",
                context.Request.Method,
                context.Request.Path.Value,
                statusCode,
                durationMs,
                requestId,
                userId,
                sanitizedQuery,
                ipAddress,
                userAgent ?? "unknown");
        }

        // B-022: Performance warning for slow requests (> 1 second)
        if (durationMs > 1000)
        {
            _logger.LogWarning(
                "Slow request detected: {Method} {Path} took {DurationMs}ms | RequestId={RequestId} Threshold=1000ms",
                context.Request.Method,
                context.Request.Path.Value,
                durationMs,
                requestId);
        }
    }

    /// <summary>
    /// B-022: Sanitizes query string by redacting sensitive parameter values.
    /// </summary>
    private static string? SanitizeQueryString(string? queryString)
    {
        if (string.IsNullOrEmpty(queryString))
            return null;

        var pairs = queryString.TrimStart('?').Split('&')
            .Select(p =>
            {
                var parts = p.Split('=', 2);
                if (parts.Length == 2 && IsSensitiveKey(parts[0]))
                    return $"{parts[0]}=[REDACTED]";
                return p;
            });

        return string.Join("&", pairs);
    }

    /// <summary>
    /// Checks if a key should be considered sensitive and redacted.
    /// </summary>
    private static bool IsSensitiveKey(string key)
    {
        // Direct match
        if (SensitiveKeys.Contains(key))
            return true;

        // Partial match for compound keys like "user_password" or "auth_token"
        var lowerKey = key.ToLowerInvariant();
        return SensitiveKeys.Any(sensitive => lowerKey.Contains(sensitive));
    }

    /// <summary>
    /// Gets the client IP address, respecting X-Forwarded-For header for proxied requests.
    /// </summary>
    private static string GetClientIpAddress(HttpContext context)
    {
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            // Take the first IP (original client) from the chain
            return forwardedFor.Split(',')[0].Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}

/// <summary>
/// Extension method for adding RequestLoggingMiddleware to the pipeline.
/// </summary>
public static class RequestLoggingMiddlewareExtensions
{
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<RequestLoggingMiddleware>();
    }
}
