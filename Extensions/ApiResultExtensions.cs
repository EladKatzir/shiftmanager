using Microsoft.AspNetCore.Mvc;
using ShiftManager.Models;

namespace ShiftManager.Extensions;

/// <summary>
/// Extension methods for creating standardized API error responses (B-028).
/// Provides convenient helper methods for controllers to return consistent error formats.
/// </summary>
public static class ApiResultExtensions
{
    /// <summary>
    /// Returns a standardized API error response with the specified status code.
    /// </summary>
    public static IActionResult ApiError(this ControllerBase controller, int statusCode, ApiErrorResponse error)
    {
        return new ObjectResult(error) { StatusCode = statusCode };
    }

    /// <summary>
    /// Returns a 400 Bad Request response with a standardized error format.
    /// </summary>
    public static IActionResult ApiBadRequest(this ControllerBase controller, string message, object? details = null)
        => controller.ApiError(400, ApiErrorResponse.BadRequest(message, details));

    /// <summary>
    /// Returns a 400 Bad Request response for validation errors.
    /// </summary>
    public static IActionResult ApiValidationError(this ControllerBase controller, IDictionary<string, string[]> errors)
        => controller.ApiError(400, ApiErrorResponse.ValidationError(errors));

    /// <summary>
    /// Returns a 400 Bad Request response for model state validation errors.
    /// </summary>
    public static IActionResult ApiValidationError(this ControllerBase controller)
        => controller.ApiError(400, ApiErrorResponse.ValidationError(controller.ModelState));

    /// <summary>
    /// Returns a 401 Unauthorized response with a standardized error format.
    /// </summary>
    public static IActionResult ApiUnauthorized(this ControllerBase controller, string? message = null)
        => controller.ApiError(401, ApiErrorResponse.Unauthorized(message ?? "Authentication required"));

    /// <summary>
    /// Returns a 403 Forbidden response with a standardized error format.
    /// </summary>
    public static IActionResult ApiForbidden(this ControllerBase controller, string? message = null)
        => controller.ApiError(403, ApiErrorResponse.Forbidden(message ?? "You don't have permission to perform this action"));

    /// <summary>
    /// Returns a 403 Forbidden response for missing grant permissions.
    /// </summary>
    public static IActionResult ApiGrantRequired(this ControllerBase controller, string grantCode)
        => controller.ApiError(403, ApiErrorResponse.GrantRequired(grantCode));

    /// <summary>
    /// Returns a 404 Not Found response with a standardized error format.
    /// </summary>
    /// <param name="controller">The controller instance</param>
    /// <param name="resource">The type of resource that was not found (e.g., "Shift", "User")</param>
    /// <param name="id">Optional identifier of the resource</param>
    public static IActionResult ApiNotFound(this ControllerBase controller, string resource, string? id = null)
        => controller.ApiError(404, ApiErrorResponse.NotFound(resource, id));

    /// <summary>
    /// Returns a 409 Conflict response with a standardized error format.
    /// </summary>
    public static IActionResult ApiConflict(this ControllerBase controller, string message, object? details = null)
        => controller.ApiError(409, ApiErrorResponse.Conflict(message, details));

    /// <summary>
    /// Returns a 409 Conflict response for schedule conflicts.
    /// </summary>
    public static IActionResult ApiScheduleConflict(this ControllerBase controller, string message, object? conflictDetails = null)
        => controller.ApiError(409, ApiErrorResponse.ScheduleConflict(message, conflictDetails));

    /// <summary>
    /// Returns a 429 Too Many Requests response.
    /// </summary>
    public static IActionResult ApiRateLimitExceeded(this ControllerBase controller, int retryAfterSeconds)
    {
        controller.Response.Headers["Retry-After"] = retryAfterSeconds.ToString();
        return controller.ApiError(429, ApiErrorResponse.RateLimitExceeded(retryAfterSeconds));
    }

    /// <summary>
    /// Returns a 500 Internal Server Error response with a standardized error format.
    /// </summary>
    public static IActionResult ApiServerError(this ControllerBase controller, string? message = null)
        => controller.ApiError(500, ApiErrorResponse.ServerError(message ?? "An unexpected error occurred"));

    /// <summary>
    /// Returns a 503 Service Unavailable response.
    /// </summary>
    public static IActionResult ApiServiceUnavailable(this ControllerBase controller, string? message = null)
        => controller.ApiError(503, ApiErrorResponse.ServiceUnavailable(message ?? "Service is temporarily unavailable"));
}
