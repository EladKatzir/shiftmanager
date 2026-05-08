using System.Diagnostics;
using ShiftManager.Data;
using ShiftManager.Models.Api;
using ShiftManager.Services;

namespace ShiftManager.Middleware;

/// <summary>
/// Logs all API requests to the database for observability and forensics.
/// Runs asynchronously to avoid blocking requests.
/// </summary>
public class ApiRequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiRequestLoggingMiddleware> _logger;
    private readonly IBackgroundTaskQueue _backgroundTaskQueue;

    public ApiRequestLoggingMiddleware(
        RequestDelegate next,
        ILogger<ApiRequestLoggingMiddleware> logger,
        IBackgroundTaskQueue backgroundTaskQueue)
    {
        _next = next;
        _logger = logger;
        _backgroundTaskQueue = backgroundTaskQueue;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only process API routes
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        // Skip logging for internal team-calendars API (uses cookie auth, not API keys)
        if (context.Request.Path.StartsWithSegments("/api/team-calendars"))
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();

        // B-022: Use RequestId from upstream RequestLoggingMiddleware (supports X-Request-ID header propagation)
        var requestId = context.Items["RequestId"]?.ToString()
            ?? context.Items["CorrelationId"]?.ToString()
            ?? Guid.NewGuid().ToString("N")[..12];

        // Store for consistency
        context.Items["RequestId"] = requestId;
        context.Items["CorrelationId"] = requestId; // Backward compatibility

        // Add correlation ID to response headers (X-Request-ID is already set by RequestLoggingMiddleware)
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Correlation-ID"] = requestId;
            return Task.CompletedTask;
        });

        Exception? caughtException = null;
        var originalBodyStream = context.Response.Body;

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

            // Capture values from HttpContext before it's disposed
            var apiKey = context.Items["ApiKey"] as ApiKey;
            var method = context.Request.Method;
            var path = context.Request.Path.ToString();
            var queryString = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : null;
            var statusCode = context.Response.StatusCode;
            var ipAddress = GetClientIpAddress(context);
            var userAgent = context.Request.Headers["User-Agent"].FirstOrDefault() ?? "Unknown";

            // Log request asynchronously (fire-and-forget via the background task queue —
            // the hosted service supplies the DI scope and surfaces unhandled exceptions).
            var capturedElapsedMs = stopwatch.ElapsedMilliseconds;
            _backgroundTaskQueue.Enqueue(async (sp, _) =>
            {
                try
                {
                    await LogRequestAsync(sp, apiKey, method, path, queryString,
                        statusCode, capturedElapsedMs, requestId, ipAddress,
                        userAgent, caughtException);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to log API request to database");
                }
            });
        }
    }

    private async Task LogRequestAsync(
        IServiceProvider scopedProvider,
        ApiKey? apiKey,
        string method,
        string path,
        string? queryString,
        int statusCode,
        long durationMs,
        string requestId,
        string ipAddress,
        string userAgent,
        Exception? exception)
    {
        var dbContext = scopedProvider.GetRequiredService<AppDbContext>();

        var log = new ApiRequestLog
        {
            ApiKeyId = apiKey?.Id,
            CompanyId = apiKey?.CompanyId,
            Method = method,
            Path = path,
            QueryString = queryString,
            StatusCode = statusCode,
            DurationMs = (int)durationMs,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            CorrelationId = requestId, // Store as CorrelationId in DB for backward compatibility
            ErrorMessage = exception?.Message,
            Timestamp = DateTime.UtcNow
        };

        dbContext.ApiRequestLogs.Add(log);
        await dbContext.SaveChangesAsync();

        // B-022: Log to structured logger with consistent field names
        if (exception != null)
        {
            _logger.LogError(exception,
                "API {Method} {Path} failed with {StatusCode} in {DurationMs}ms | RequestId={RequestId} ApiKeyId={ApiKeyId} IP={IpAddress} UserAgent={UserAgent} Error={ErrorType}",
                method, path, statusCode, durationMs, requestId, apiKey?.Id, ipAddress, userAgent, exception.GetType().Name);
        }
        else
        {
            _logger.LogInformation(
                "API {Method} {Path} completed with {StatusCode} in {DurationMs}ms | RequestId={RequestId} ApiKeyId={ApiKeyId} IP={IpAddress} UserAgent={UserAgent}",
                method, path, statusCode, durationMs, requestId, apiKey?.Id, ipAddress, userAgent);
        }
    }

    // NOTE: X-Forwarded-For is trusted without validation. This is acceptable for air-gapped IIS deployment
    // where only the IIS reverse proxy sets this header. If deployment moves to public network, configure
    // ASP.NET Core ForwardedHeadersMiddleware with known proxy IPs instead.
    private string GetClientIpAddress(HttpContext context)
    {
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
    }
}
