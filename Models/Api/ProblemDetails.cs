using System.Text.Json.Serialization;

namespace ShiftManager.Models.Api;

/// <summary>
/// RFC-7807 Problem Details for HTTP APIs
/// https://datatracker.ietf.org/doc/html/rfc7807
/// </summary>
public class ApiProblemDetails
{
    /// <summary>
    /// A URI reference that identifies the problem type
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "about:blank";

    /// <summary>
    /// A short, human-readable summary of the problem type
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// The HTTP status code
    /// </summary>
    [JsonPropertyName("status")]
    public int Status { get; set; }

    /// <summary>
    /// A human-readable explanation specific to this occurrence of the problem
    /// </summary>
    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    /// <summary>
    /// A URI reference that identifies the specific occurrence of the problem
    /// </summary>
    [JsonPropertyName("instance")]
    public string? Instance { get; set; }

    /// <summary>
    /// Additional extension members (for validation errors, etc.)
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, object>? Extensions { get; set; }

    /// <summary>
    /// Helper method to create a validation error problem
    /// </summary>
    public static ApiProblemDetails ValidationError(string detail, string? instance = null, Dictionary<string, string[]>? errors = null)
    {
        var problem = new ApiProblemDetails
        {
            Type = "about:blank",
            Title = "Validation Error",
            Status = 400,
            Detail = detail,
            Instance = instance
        };

        if (errors != null && errors.Any())
        {
            problem.Extensions = new Dictionary<string, object>
            {
                ["errors"] = errors
            };
        }

        return problem;
    }

    /// <summary>
    /// Helper method to create a not found problem
    /// </summary>
    public static ApiProblemDetails NotFound(string detail, string? instance = null)
    {
        return new ApiProblemDetails
        {
            Type = "about:blank",
            Title = "Not Found",
            Status = 404,
            Detail = detail,
            Instance = instance
        };
    }

    /// <summary>
    /// Helper method to create an unauthorized problem
    /// </summary>
    public static ApiProblemDetails Unauthorized(string detail = "Invalid or missing API key", string? instance = null)
    {
        return new ApiProblemDetails
        {
            Type = "about:blank",
            Title = "Unauthorized",
            Status = 401,
            Detail = detail,
            Instance = instance
        };
    }

    /// <summary>
    /// Helper method to create a forbidden problem
    /// </summary>
    public static ApiProblemDetails Forbidden(string detail = "Insufficient permissions", string? instance = null)
    {
        return new ApiProblemDetails
        {
            Type = "about:blank",
            Title = "Forbidden",
            Status = 403,
            Detail = detail,
            Instance = instance
        };
    }

    /// <summary>
    /// Helper method to create a conflict problem
    /// </summary>
    public static ApiProblemDetails Conflict(string detail, string? instance = null)
    {
        return new ApiProblemDetails
        {
            Type = "about:blank",
            Title = "Conflict",
            Status = 409,
            Detail = detail,
            Instance = instance
        };
    }

    /// <summary>
    /// Helper method to create a rate limit problem
    /// </summary>
    public static ApiProblemDetails RateLimitExceeded(int retryAfterSeconds, string? instance = null)
    {
        var problem = new ApiProblemDetails
        {
            Type = "about:blank",
            Title = "Too Many Requests",
            Status = 429,
            Detail = $"Rate limit exceeded. Retry after {retryAfterSeconds} seconds.",
            Instance = instance
        };

        problem.Extensions = new Dictionary<string, object>
        {
            ["retryAfter"] = retryAfterSeconds
        };

        return problem;
    }

    /// <summary>
    /// Helper method to create an internal server error problem
    /// </summary>
    public static ApiProblemDetails InternalError(string detail = "An unexpected error occurred", string? instance = null)
    {
        return new ApiProblemDetails
        {
            Type = "about:blank",
            Title = "Internal Server Error",
            Status = 500,
            Detail = detail,
            Instance = instance
        };
    }
}
