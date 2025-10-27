using System.Diagnostics;

namespace ShiftManager.Middleware;

/// <summary>
/// Middleware for logging HTTP requests with performance metrics and correlation IDs.
/// </summary>
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Generate correlation ID for request tracking
        var correlationId = Guid.NewGuid().ToString("N")[..12]; // Short correlation ID
        context.Items["CorrelationId"] = correlationId;

        var stopwatch = Stopwatch.StartNew();
        var request = context.Request;

        // Get user information if authenticated
        var userId = context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        try
        {
            // Log request start
            _logger.LogInformation(
                "REQUEST START | CorrelationId={CorrelationId} Method={Method} Path={Path} UserId={UserId} IP={IpAddress}",
                correlationId, request.Method, request.Path, userId, ipAddress);

            await _next(context);

            stopwatch.Stop();

            // Log request completion
            var statusCode = context.Response.StatusCode;
            var logLevel = statusCode >= 500 ? LogLevel.Error :
                          statusCode >= 400 ? LogLevel.Warning :
                          LogLevel.Information;

            _logger.Log(logLevel,
                "REQUEST END | CorrelationId={CorrelationId} Method={Method} Path={Path} StatusCode={StatusCode} Duration={Duration}ms UserId={UserId}",
                correlationId, request.Method, request.Path, statusCode, stopwatch.ElapsedMilliseconds, userId);

            // Log slow requests (over 1 second)
            if (stopwatch.ElapsedMilliseconds > 1000)
            {
                _logger.LogWarning(
                    "PERFORMANCE: Slow request | CorrelationId={CorrelationId} Path={Path} Duration={Duration}ms",
                    correlationId, request.Path, stopwatch.ElapsedMilliseconds);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(ex,
                "REQUEST ERROR | CorrelationId={CorrelationId} Method={Method} Path={Path} Duration={Duration}ms UserId={UserId} Error={ErrorMessage}",
                correlationId, request.Method, request.Path, stopwatch.ElapsedMilliseconds, userId, ex.Message);

            throw;
        }
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
