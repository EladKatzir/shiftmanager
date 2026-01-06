using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models.Api;

namespace ShiftManager.Middleware;

/// <summary>
/// Authenticates API requests using X-API-Key header.
/// Validates API key, checks scopes, and sets up claims for downstream authorization.
/// </summary>
public class ApiAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiAuthenticationMiddleware> _logger;

    public ApiAuthenticationMiddleware(RequestDelegate next, ILogger<ApiAuthenticationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, AppDbContext dbContext)
    {
        // Only process API routes
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        // Check if this is an internal web UI endpoint (uses cookie auth, not API keys)
        // These are Razor Page endpoints used by authenticated users in the browser
        if (IsInternalWebUiEndpoint(context.Request.Path))
        {
            // Game endpoints allow anonymous access (they handle auth internally)
            if (context.Request.Path.StartsWithSegments("/Api/Game", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            // Other internal endpoints require authentication
            // If user is already authenticated via cookies, allow request
            if (context.User?.Identity?.IsAuthenticated == true)
            {
                await _next(context);
                return;
            }
            // If not authenticated via cookies, return 401
            _logger.LogWarning("Unauthenticated request to internal API endpoint: {Path}", context.Request.Path);
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Unauthorized");
            return;
        }

        var apiKeyHeader = context.Request.Headers["X-API-Key"].FirstOrDefault();

        // Missing API key
        if (string.IsNullOrEmpty(apiKeyHeader))
        {
            await WriteUnauthorizedResponse(context, "Missing X-API-Key header");
            return;
        }

        // Hash the provided key to compare with stored hash
        var keyHash = HashApiKey(apiKeyHeader);

        // Look up API key in database
        var apiKey = await dbContext.ApiKeys
            .Include(k => k.Company)
            .FirstOrDefaultAsync(k => k.KeyHash == keyHash);

        if (apiKey == null)
        {
            _logger.LogWarning("Invalid API key attempt from IP: {IpAddress}", GetClientIpAddress(context));
            await WriteUnauthorizedResponse(context, "Invalid API key");
            return;
        }

        // Validate API key status
        if (!apiKey.IsValid())
        {
            var reason = !apiKey.IsActive ? "API key is inactive" :
                         apiKey.ExpiresAt.HasValue && apiKey.ExpiresAt.Value < DateTime.UtcNow ? "API key has expired" :
                         "API key is invalid";

            _logger.LogWarning("Invalid API key used: {Reason}, CompanyId: {CompanyId}, KeyId: {KeyId}",
                reason, apiKey.CompanyId, apiKey.Id);
            await WriteUnauthorizedResponse(context, reason);
            return;
        }

        // Check scope permissions for this endpoint
        var requiredScope = DetermineRequiredScope(context.Request.Path, context.Request.Method);
        if (!string.IsNullOrEmpty(requiredScope) && !apiKey.HasScope(requiredScope))
        {
            _logger.LogWarning("Insufficient scope for API key. Required: {RequiredScope}, CompanyId: {CompanyId}, KeyId: {KeyId}",
                requiredScope, apiKey.CompanyId, apiKey.Id);
            await WriteForbiddenResponse(context, $"Insufficient permissions. Required scope: {requiredScope}");
            return;
        }

        // Set up claims for the authenticated API request
        var claims = new List<Claim>
        {
            new Claim("ApiKeyId", apiKey.Id.ToString()),
            new Claim("CompanyId", apiKey.CompanyId.ToString()),
            new Claim("ApiKeyName", apiKey.Name),
            new Claim("UserId", apiKey.CreatedBy.ToString()), // Add UserId for controllers that need it
            new Claim("AuthenticationType", "ApiKey")
        };

        // Add scope claims
        var scopes = apiKey.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var scope in scopes)
        {
            claims.Add(new Claim("scope", scope.Trim()));
        }

        var identity = new ClaimsIdentity(claims, "ApiKey");
        context.User = new ClaimsPrincipal(identity);

        // Store API key info in HttpContext.Items for rate limiting and logging
        context.Items["ApiKey"] = apiKey;
        context.Items["CorrelationId"] = Guid.NewGuid().ToString();

        // Update last used timestamp (fire-and-forget, don't block request)
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = context.RequestServices.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var key = await db.ApiKeys.FindAsync(apiKey.Id);
                if (key != null)
                {
                    key.LastUsedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update LastUsedAt for API key {KeyId}", apiKey.Id);
            }
        });

        await _next(context);
    }

    /// <summary>
    /// Determines the required scope based on the endpoint path and HTTP method
    /// </summary>
    private string DetermineRequiredScope(PathString path, string method)
    {
        // Extract resource from path (e.g., /api/v1/users -> users)
        var segments = path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
        if (segments.Length < 3) return string.Empty; // Invalid API path

        var resource = segments[2]; // users, shifts, time-off-requests, etc.

        // Map HTTP method to operation
        var operation = method.ToUpperInvariant() switch
        {
            "GET" => "read",
            "POST" => "write",
            "PUT" => "write",
            "PATCH" => "write",
            "DELETE" => "write",
            _ => "read"
        };

        // Special cases for specific endpoints
        if (path.StartsWithSegments("/api/v1/analytics")) return "analytics:read";
        if (path.StartsWithSegments("/api/v1/audit-logs")) return "audit:read";
        if (path.StartsWithSegments("/api/v1/webhooks")) return "webhook:manage";

        // Standard resource:operation format (e.g., user:read, shift:write)
        var resourceName = resource.TrimEnd('s'); // users -> user
        return $"{resourceName}:{operation}";
    }

    /// <summary>
    /// Determines if the path is an internal web UI API endpoint that should use cookie auth
    /// rather than API key authentication.
    /// These are Razor Page endpoints used by authenticated users in the browser.
    /// </summary>
    private bool IsInternalWebUiEndpoint(PathString path)
    {
        // Team calendars - existing endpoint
        if (path.StartsWithSegments("/api/team-calendars", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Session status - used by session-check.js for browser session management
        if (path.StartsWithSegments("/Api/SessionStatus", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Calendar quick-action endpoints - used by calendar-inline-edit.js for inline CRUD
        if (path.StartsWithSegments("/Api/Calendar", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Game endpoints - used by shift-swap-game.js for browser-based game functionality
        if (path.StartsWithSegments("/Api/Game", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Griffin ADFS callback - receives authentication token from Griffin service
        if (path.StartsWithSegments("/Auth/GriffinCallback", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Localization API - used by localization-api.js for client-side string fetching
        if (path.StartsWithSegments("/Api/Localization", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Hashes an API key using SHA256
    /// </summary>
    private string HashApiKey(string apiKey)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(apiKey);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Writes a 401 Unauthorized response
    /// </summary>
    private async Task WriteUnauthorizedResponse(HttpContext context, string detail)
    {
        context.Response.StatusCode = 401;
        context.Response.ContentType = "application/problem+json";

        var problem = ApiProblemDetails.Unauthorized(detail, context.Request.Path);
        await context.Response.WriteAsJsonAsync(problem);
    }

    /// <summary>
    /// Writes a 403 Forbidden response
    /// </summary>
    private async Task WriteForbiddenResponse(HttpContext context, string detail)
    {
        context.Response.StatusCode = 403;
        context.Response.ContentType = "application/problem+json";

        var problem = ApiProblemDetails.Forbidden(detail, context.Request.Path);
        await context.Response.WriteAsJsonAsync(problem);
    }

    /// <summary>
    /// Gets the client IP address, handling proxies
    /// </summary>
    private string GetClientIpAddress(HttpContext context)
    {
        // Check X-Forwarded-For header first (for proxies/load balancers)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
    }
}
