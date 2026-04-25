using ShiftManager.Models.Results;

namespace ShiftManager.Models;

/// <summary>
/// Standardized API error response format (B-028)
/// All API errors return: { "error": { "code": "string", "message": "string", "details": {}, "correlationId": "..." } }
/// </summary>
public class ApiErrorResponse
{
    public ApiError Error { get; set; } = new();

    /// <summary>
    /// Attach the request correlation ID so the user can quote it to support.
    /// Mutates and returns the same instance for fluent chaining.
    /// </summary>
    public ApiErrorResponse WithCorrelationId(string correlationId)
    {
        Error.CorrelationId = correlationId;
        return this;
    }

    /// <summary>
    /// Build an ApiErrorResponse from a failed <see cref="OperationResult"/>.
    /// The caller chooses the API error <paramref name="code"/> (typically the result's <c>ErrorKey</c>
    /// or a route-specific override). All issues are surfaced under <c>details.issues</c>.
    /// </summary>
    public static ApiErrorResponse FromOperationResult(OperationResult result, string code)
    {
        return Create(
            code,
            result.ErrorMessage ?? "Operation failed",
            result.Issues.Count > 0 ? (object)new { issues = result.Issues } : null);
    }

    /// <summary>
    /// Creates a new API error response with the specified code, message, and optional details.
    /// </summary>
    /// <param name="code">Machine-readable error code (e.g., "SHIFT_NOT_FOUND", "GRANT_REQUIRED")</param>
    /// <param name="message">User-friendly error message</param>
    /// <param name="details">Optional additional details (validation errors, etc.)</param>
    public static ApiErrorResponse Create(string code, string message, object? details = null)
    {
        return new ApiErrorResponse
        {
            Error = new ApiError
            {
                Code = code,
                Message = message,
                Details = details
            }
        };
    }

    /// <summary>
    /// Creates a 400 Bad Request error response.
    /// </summary>
    public static ApiErrorResponse BadRequest(string message, object? details = null)
        => Create("BAD_REQUEST", message, details);

    /// <summary>
    /// Creates a 401 Unauthorized error response.
    /// </summary>
    public static ApiErrorResponse Unauthorized(string message = "Authentication required")
        => Create("UNAUTHORIZED", message);

    /// <summary>
    /// Creates a 403 Forbidden error response.
    /// </summary>
    public static ApiErrorResponse Forbidden(string message = "You don't have permission to perform this action")
        => Create("FORBIDDEN", message);

    /// <summary>
    /// Creates a 403 Forbidden error response for missing grants.
    /// </summary>
    public static ApiErrorResponse GrantRequired(string grantCode)
        => Create("GRANT_REQUIRED", $"You need the '{grantCode}' permission to perform this action");

    /// <summary>
    /// Creates a 404 Not Found error response.
    /// </summary>
    /// <param name="resource">The type of resource (e.g., "Shift", "User")</param>
    /// <param name="id">Optional resource identifier</param>
    public static ApiErrorResponse NotFound(string resource, string? id = null)
        => Create($"{resource.ToUpperInvariant()}_NOT_FOUND", $"{resource} not found" + (id != null ? $": {id}" : ""));

    /// <summary>
    /// Creates a 409 Conflict error response.
    /// </summary>
    public static ApiErrorResponse Conflict(string message, object? details = null)
        => Create("CONFLICT", message, details);

    /// <summary>
    /// Creates a 409 Conflict error response for schedule conflicts.
    /// </summary>
    public static ApiErrorResponse ScheduleConflict(string message, object? conflictDetails = null)
        => Create("SCHEDULE_CONFLICT", message, conflictDetails);

    /// <summary>
    /// Creates a 409 Conflict error response for concurrent edit conflicts.
    /// This is returned when two users try to edit the same entity simultaneously.
    /// </summary>
    /// <param name="entityType">The type of entity that had the conflict (e.g., "Shift", "Assignment")</param>
    /// <param name="entityId">The ID of the entity</param>
    public static ApiErrorResponse ConcurrencyConflict(string entityType, int? entityId = null)
        => Create("CONCURRENCY_CONFLICT", 
            "This record was modified by another user. Please reload and try again.",
            new { entityType, entityId, conflictType = "concurrent_edit" });

    /// <summary>
    /// Creates a 400 Validation Error response with field-level details.
    /// </summary>
    /// <param name="errors">Dictionary of field names to error messages</param>
    public static ApiErrorResponse ValidationError(IDictionary<string, string[]> errors)
        => Create("VALIDATION_ERROR", "One or more validation errors occurred", errors);

    /// <summary>
    /// Creates a 400 Validation Error response from ASP.NET ModelState.
    /// </summary>
    public static ApiErrorResponse ValidationError(Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary modelState)
    {
        var errors = modelState
            .Where(kvp => kvp.Value?.Errors.Count > 0)
            .ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
            );
        return ValidationError(errors);
    }

    /// <summary>
    /// Creates a 429 Too Many Requests error response.
    /// </summary>
    public static ApiErrorResponse RateLimitExceeded(int retryAfterSeconds)
        => Create("RATE_LIMIT_EXCEEDED", $"Rate limit exceeded. Retry after {retryAfterSeconds} seconds",
            new { retryAfterSeconds });

    /// <summary>
    /// Creates a 500 Internal Server Error response.
    /// Does not expose stack traces or internal details.
    /// </summary>
    public static ApiErrorResponse ServerError(string message = "An unexpected error occurred")
        => Create("SERVER_ERROR", message);

    /// <summary>
    /// Creates a 503 Service Unavailable error response.
    /// </summary>
    public static ApiErrorResponse ServiceUnavailable(string message = "Service is temporarily unavailable")
        => Create("SERVICE_UNAVAILABLE", message);
}

/// <summary>
/// The error object within an API error response.
/// </summary>
public class ApiError
{
    /// <summary>
    /// Machine-readable error code (e.g., "SHIFT_NOT_FOUND", "GRANT_REQUIRED", "VALIDATION_ERROR")
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// User-friendly error message suitable for display.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Optional additional details (validation errors, conflict info, etc.)
    /// </summary>
    public object? Details { get; set; }

    /// <summary>
    /// Request correlation ID (X-Correlation-Id). Surfaced to users in the FeedbackModal/Toast
    /// so they can quote it to support, and recorded in the structured log scope for any
    /// log entries produced while serving the request.
    /// </summary>
    public string? CorrelationId { get; set; }
}
