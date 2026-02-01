using System.Net;
using System.Text.Json;
using ShiftManager.Models;

namespace ShiftManager.Middleware;

/// <summary>
/// Global exception handler middleware for API routes (B-028).
/// Converts unhandled exceptions to standardized JSON error responses.
/// </summary>
public class ApiExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiExceptionMiddleware> _logger;

    public ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        // Only handle API requests - let non-API requests propagate to default handlers
        if (!IsApiRequest(context.Request.Path))
        {
            throw exception;
        }

        // Log the exception with correlation info
        var correlationId = context.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();
        _logger.LogError(exception,
            "Unhandled exception in API request. Path={Path}, Method={Method}, CorrelationId={CorrelationId}",
            context.Request.Path,
            context.Request.Method,
            correlationId);

        // Don't overwrite response if it has already started
        if (context.Response.HasStarted)
        {
            _logger.LogWarning("Response already started, cannot write error response. CorrelationId={CorrelationId}",
                correlationId);
            throw exception;
        }

        context.Response.ContentType = "application/json";

        var (statusCode, response) = MapExceptionToResponse(exception, correlationId);

        context.Response.StatusCode = statusCode;

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        await context.Response.WriteAsync(json);
    }

    /// <summary>
    /// Maps an exception to the appropriate HTTP status code and error response.
    /// </summary>
    private (int statusCode, ApiErrorResponse response) MapExceptionToResponse(Exception exception, string correlationId)
    {
        return exception switch
        {
            // Authentication/Authorization
            UnauthorizedAccessException => (
                StatusCodes.Status401Unauthorized,
                ApiErrorResponse.Unauthorized()),

            // Not Found
            KeyNotFoundException ex => (
                StatusCodes.Status404NotFound,
                ApiErrorResponse.Create("RESOURCE_NOT_FOUND", ex.Message ?? "Resource not found")),

            // Validation / Bad Input (more specific types first)
            ArgumentNullException ex => (
                StatusCodes.Status400BadRequest,
                ApiErrorResponse.BadRequest($"Required parameter '{ex.ParamName}' is missing")),

            ArgumentException ex => (
                StatusCodes.Status400BadRequest,
                ApiErrorResponse.BadRequest(ex.Message)),

            FormatException ex => (
                StatusCodes.Status400BadRequest,
                ApiErrorResponse.BadRequest($"Invalid format: {ex.Message}")),

            // Business Logic Errors
            InvalidOperationException ex => (
                StatusCodes.Status400BadRequest,
                ApiErrorResponse.BadRequest(ex.Message)),

            // Concurrency / Conflict (B-018)
            Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException ex => (
                StatusCodes.Status409Conflict,
                ApiErrorResponse.ConcurrencyConflict(
                    ex.Entries.FirstOrDefault()?.Entity?.GetType().Name ?? "Unknown")),

            // Operation Cancelled (client disconnected)
            OperationCanceledException => (
                499, // Client Closed Request - Non-standard code used by nginx
                ApiErrorResponse.Create("REQUEST_CANCELLED", "The request was cancelled")),

            // Timeout
            TimeoutException => (
                StatusCodes.Status504GatewayTimeout,
                ApiErrorResponse.Create("TIMEOUT", "The operation timed out")),

            // Default - Internal Server Error
            // Never expose stack traces or internal exception details
            _ => (
                StatusCodes.Status500InternalServerError,
                ApiErrorResponse.ServerError())
        };
    }

    /// <summary>
    /// Determines if the request path is an API endpoint.
    /// </summary>
    private bool IsApiRequest(PathString path)
    {
        // All API routes start with /api
        if (path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Also handle /Api routes (case variation for Razor Pages API endpoints)
        if (path.StartsWithSegments("/Api", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}

