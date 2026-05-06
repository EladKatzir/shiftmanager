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

        // FINDING-001 FIX: Version endpoint is truly anonymous — no auth required
        if (context.Request.Path.StartsWithSegments("/api/v1/version", StringComparison.OrdinalIgnoreCase))
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

            // Localization API allows anonymous access (needed for login page and unauthenticated UI)
            if (context.Request.Path.StartsWithSegments("/Api/Localization", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            // Signup API allows anonymous access (cascading dropdowns on public signup page)
            if (context.Request.Path.StartsWithSegments("/Api/Signup", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            // Telemetry API allows anonymous access (needed for login page metrics and error reporting)
            // Has its own rate limiting (30 req/min per IP) and [IgnoreAntiforgeryToken]
            if (context.Request.Path.StartsWithSegments("/Api/Telemetry", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            // SessionStatus allows anonymous access (session-check.js polls it for expiration)
            // Page handler has [AllowAnonymous] and returns JSON { authenticated, state } with 401
            if (context.Request.Path.StartsWithSegments("/Api/SessionStatus", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            // Other internal endpoints require authentication
            // If user is already authenticated via cookies, allow request
            if (context.User?.Identity?.IsAuthenticated == true)
            {
                // CSRF protection: Require X-Requested-With header for state-changing requests
                // that use cookie auth (fixes D-03). GET/HEAD/OPTIONS are safe from CSRF.
                var method = context.Request.Method;
                if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsOptions(method))
                {
                    var xRequestedWith = context.Request.Headers["X-Requested-With"].FirstOrDefault();
                    if (!string.Equals(xRequestedWith, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning(
                            "CSRF protection: Rejected {Method} request to {Path} without X-Requested-With header from user {User}",
                            method, context.Request.Path, context.User.Identity?.Name);
                        context.Response.StatusCode = 403;
                        await context.Response.WriteAsync("Forbidden: Missing X-Requested-With header");
                        return;
                    }
                }
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
            .IgnoreQueryFilters()  // SECURITY-AUDITED: Runs before tenant context; key lookup establishes tenant
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

        // A-09: Debounced LastUsedAt update — only update if not updated in the last 60 seconds
        // This prevents SQLITE_BUSY from concurrent fire-and-forget writes
        var lastUpdated = _lastUsedCache.GetValueOrDefault(apiKey.Id);
        if ((DateTime.UtcNow - lastUpdated).TotalSeconds > 60)
        {
            _lastUsedCache[apiKey.Id] = DateTime.UtcNow;

            // Periodic cleanup: every 100 updates, check if cache exceeds 1000 entries
            // and remove entries older than 24 hours to prevent unbounded growth
            var updates = System.Threading.Interlocked.Increment(ref _updatesSinceLastCleanupCheck);
            if (updates % 100 == 0 && _lastUsedCache.Count > 1000)
            {
                var cutoff = DateTime.UtcNow.AddHours(-24);
                var staleKeys = _lastUsedCache
                    .Where(kvp => kvp.Value < cutoff)
                    .Select(kvp => kvp.Key)
                    .ToList();
                foreach (var staleKey in staleKeys)
                    _lastUsedCache.TryRemove(staleKey, out _);

                _logger.LogDebug("Cleaned up {Count} stale entries from API key LastUsedAt cache", staleKeys.Count);
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = context.RequestServices.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var key = await db.ApiKeys
                        .IgnoreQueryFilters()  // SECURITY-AUDITED: Background update of authenticated key's last-used timestamp
                        .FirstOrDefaultAsync(k => k.Id == apiKey.Id);
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
        }

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

        // D-07: Use explicit mapping instead of naive TrimEnd('s') which breaks
        // "analytics" → "analytic", "status" → "statu", etc.
        var resourceName = resource.ToLowerInvariant() switch
        {
            "users" => "user",
            "shifts" => "shift",
            "chores" => "chore",
            "notifications" => "notification",
            "swap-requests" => "swap-request",
            "time-off-requests" => "time-off",
            "analytics" => "analytics",
            "audit-logs" => "audit",
            "feedback" => "feedback",
            "onduty" => "onduty",
            "on-duty" => "onduty",
            "webhooks" => "webhook",
            _ => resource.ToLowerInvariant()
        };
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

        // Scope Switcher API - used by scope-switcher component for fetching available scopes
        if (path.StartsWithSegments("/Api/ScopeSwitcher", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // SelectMolecule API - used by context switcher for director molecule selection
        if (path.StartsWithSegments("/Api/SelectMolecule", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Signup API - used by public signup page for cascading dropdowns (molecule → company → job type)
        if (path.StartsWithSegments("/Api/Signup", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Telemetry API - used by telemetry.js for client-side analytics and error reporting
        if (path.StartsWithSegments("/Api/Telemetry", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Schedule Export API - used by schedule export feature for PDF/Excel/CSV generation
        if (path.StartsWithSegments("/Api/ScheduleExport", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Hierarchy API - used by HierarchyTree component for org structure management
        if (path.StartsWithSegments("/Api/Hierarchy", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // OnDuty API - used by OnDuty page for eligible users dropdown
        if (path.StartsWithSegments("/Api/OnDuty", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Admin grant verification/repair API - used by Owner pages for grant management
        if (path.StartsWithSegments("/api/admin/verify-grants", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Friends API - used by friends-highlight.js for calendar friend highlighting
        if (path.StartsWithSegments("/Api/Friends", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Widget API - used by Quick Info widget for config save/load
        if (path.StartsWithSegments("/Api/Widget", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // My API - per-user preferences endpoints (theme picker, future user-level config).
        // Cookie-authenticated browser flow; always scoped to the current user claim.
        if (path.StartsWithSegments("/Api/My", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Task 28: TimeOffRequest API - used by cancel-or-shorten-dialog.js for the
        // × button on vacation/after-derived HOME chips (cancel entire / shorten range)
        if (path.StartsWithSegments("/Api/TimeOffRequest", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Hashes an API key using HMAC-SHA256 with a server secret.
    /// Prevents rainbow table attacks on DB compromise (fixes D-04).
    /// </summary>
    internal static string HashApiKey(string apiKey, string? hmacSecret = null)
    {
        var secret = hmacSecret ?? HmacSecret;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToBase64String(hash);
    }

    // Server-wide HMAC secret for API key hashing. In production, load from config or Data Protection.
    // This is intentionally a constant fallback — override via IConfiguration "ApiKeyHmacSecret" at startup.
    internal static string HmacSecret { get; set; } = "ShiftManager-ApiKey-HMAC-v1-Default";

    // A-09: Debounce cache for LastUsedAt updates — prevents SQLITE_BUSY from concurrent writes
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, DateTime> _lastUsedCache = new();
    private static int _updatesSinceLastCleanupCheck = 0;

    /// <summary>
    /// Writes a 401 Unauthorized response
    /// </summary>
    private async Task WriteUnauthorizedResponse(HttpContext context, string detail)
    {
        context.Response.StatusCode = 401;
        context.Response.ContentType = "application/json";

        var problem = ShiftManager.Models.ApiErrorResponse.Unauthorized(detail);
        await context.Response.WriteAsJsonAsync(problem);
    }

    /// <summary>
    /// Writes a 403 Forbidden response
    /// </summary>
    private async Task WriteForbiddenResponse(HttpContext context, string detail)
    {
        context.Response.StatusCode = 403;
        context.Response.ContentType = "application/json";

        var problem = ShiftManager.Models.ApiErrorResponse.Forbidden(detail);
        await context.Response.WriteAsJsonAsync(problem);
    }

    /// <summary>
    /// Gets the client IP address, handling proxies
    /// </summary>
    // NOTE: X-Forwarded-For is trusted without validation. This is acceptable for air-gapped IIS deployment
    // where only the IIS reverse proxy sets this header. If deployment moves to public network, configure
    // ASP.NET Core ForwardedHeadersMiddleware with known proxy IPs instead.
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
