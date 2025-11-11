using System.Diagnostics;
using ShiftManager.Data;
using ShiftManager.Models.Api;

namespace ShiftManager.Middleware;

/// <summary>
/// Logs all API requests to the database for observability and forensics.
/// Runs asynchronously to avoid blocking requests.
/// </summary>
public class ApiRequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiRequestLoggingMiddleware> _logger;

    public ApiRequestLoggingMiddleware(RequestDelegate next, ILogger<ApiRequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IServiceScopeFactory scopeFactory)
    {
        // Only process API routes
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var correlationId = context.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();

        // Store correlation ID for later use
        context.Items["CorrelationId"] = correlationId;

        // Add correlation ID to response headers
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Correlation-ID"] = correlationId;
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

            // Log request asynchronously (fire-and-forget)
            _ = Task.Run(async () =>
            {
                try
                {
                    await LogRequestAsync(scopeFactory, context, stopwatch.ElapsedMilliseconds, correlationId, caughtException);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to log API request to database");
                }
            });
        }
    }

    private async Task LogRequestAsync(
        IServiceScopeFactory scopeFactory,
        HttpContext context,
        long durationMs,
        string correlationId,
        Exception? exception)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var apiKey = context.Items["ApiKey"] as ApiKey;

        var log = new ApiRequestLog
        {
            ApiKeyId = apiKey?.Id,
            CompanyId = apiKey?.CompanyId,
            Method = context.Request.Method,
            Path = context.Request.Path,
            QueryString = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : null,
            StatusCode = context.Response.StatusCode,
            DurationMs = (int)durationMs,
            IpAddress = GetClientIpAddress(context),
            UserAgent = context.Request.Headers["User-Agent"].FirstOrDefault() ?? "Unknown",
            CorrelationId = correlationId,
            ErrorMessage = exception?.Message,
            Timestamp = DateTime.UtcNow
        };

        dbContext.ApiRequestLogs.Add(log);
        await dbContext.SaveChangesAsync();

        // Log to structured logger as well
        if (exception != null)
        {
            _logger.LogError(exception,
                "API request failed: {Method} {Path} - Status: {StatusCode}, Duration: {Duration}ms, CorrelationId: {CorrelationId}",
                log.Method, log.Path, log.StatusCode, log.DurationMs, correlationId);
        }
        else
        {
            _logger.LogInformation(
                "API request: {Method} {Path} - Status: {StatusCode}, Duration: {Duration}ms, CorrelationId: {CorrelationId}",
                log.Method, log.Path, log.StatusCode, log.DurationMs, correlationId);
        }
    }

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
