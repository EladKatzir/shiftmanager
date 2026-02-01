using Microsoft.Extensions.Caching.Memory;
using System.Net;

namespace ShiftManager.Middleware;

/// <summary>
/// Rate limits UI API requests based on endpoint path and user identity.
/// Uses a sliding window algorithm for smooth rate limiting.
/// B-027: Rate Limiting for UI Endpoints
///
/// Configured limits:
/// - Calendar API: 60 requests/minute/user
/// - Context Switcher API: 30 requests/minute/user
/// - Widget API: 30 requests/minute/user
/// - Telemetry API: 30 requests/minute/user
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly ILogger<RateLimitingMiddleware> _logger;

    /// <summary>
    /// Rate limit configuration for UI endpoints.
    /// Key: URL path prefix (case-insensitive matching)
    /// Value: (requests per minute, time window)
    /// </summary>
    private static readonly Dictionary<string, (int Limit, TimeSpan Window)> EndpointLimits = new()
    {
        // Calendar endpoints - 60 req/min (higher limit for frequent calendar views)
        { "/api/calendar", (60, TimeSpan.FromMinutes(1)) },
        { "/Api/Calendar", (60, TimeSpan.FromMinutes(1)) },
        { "/api/team-calendars", (60, TimeSpan.FromMinutes(1)) },

        // Context Switcher endpoints - 30 req/min
        { "/api/context", (30, TimeSpan.FromMinutes(1)) },
        { "/Api/Context", (30, TimeSpan.FromMinutes(1)) },

        // Widget endpoints - 30 req/min
        { "/api/widget", (30, TimeSpan.FromMinutes(1)) },
        { "/Api/Widget", (30, TimeSpan.FromMinutes(1)) },

        // Telemetry endpoints - 30 req/min (also handled internally but middleware adds headers)
        { "/Api/Telemetry", (30, TimeSpan.FromMinutes(1)) }
    };

    public RateLimitingMiddleware(RequestDelegate next, IMemoryCache cache, ILogger<RateLimitingMiddleware> logger)
    {
        _next = next;
        _cache = cache;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        var limit = GetLimitForPath(path);

        // Skip rate limiting for paths not in our configuration
        if (limit == null)
        {
            await _next(context);
            return;
        }

        var clientKey = GetClientKey(context);
        var endpointKey = GetEndpointKey(path);
        var cacheKey = $"ratelimit:ui:{endpointKey}:{clientKey}";

        // Get or create rate limit bucket for this client+endpoint combination
        var bucket = _cache.GetOrCreate(cacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = limit.Value.Window;
            return new RateLimitBucket(limit.Value.Limit, limit.Value.Window);
        });

        if (bucket == null)
        {
            // Should not happen, but safety check
            await _next(context);
            return;
        }

        // Check if request is allowed
        if (!bucket.TryConsume())
        {
            var retryAfterSeconds = bucket.GetRetryAfterSeconds();

            _logger.LogWarning(
                "UI rate limit exceeded for {Path} by {ClientKey}. Retry after {RetryAfter}s",
                path, clientKey, retryAfterSeconds);

            await WriteRateLimitResponse(context, limit.Value.Limit, bucket.GetRemainingTokens(),
                bucket.GetResetTimestamp(), retryAfterSeconds);
            return;
        }

        // Update cache with modified bucket
        _cache.Set(cacheKey, bucket, limit.Value.Window);

        // Add rate limit headers to successful response
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-RateLimit-Limit"] = limit.Value.Limit.ToString();
            context.Response.Headers["X-RateLimit-Remaining"] = bucket.GetRemainingTokens().ToString();
            context.Response.Headers["X-RateLimit-Reset"] = bucket.GetResetTimestamp().ToString();
            return Task.CompletedTask;
        });

        await _next(context);
    }

    /// <summary>
    /// Gets the rate limit configuration for a given path.
    /// Returns null if no rate limit applies.
    /// </summary>
    private (int Limit, TimeSpan Window)? GetLimitForPath(string path)
    {
        foreach (var (prefix, limit) in EndpointLimits)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return limit;
            }
        }
        return null;
    }

    /// <summary>
    /// Gets a normalized endpoint key for caching.
    /// Groups similar endpoints together (e.g., /Api/Calendar/* all count against Calendar limit).
    /// </summary>
    private string GetEndpointKey(string path)
    {
        var lowerPath = path.ToLowerInvariant();

        if (lowerPath.StartsWith("/api/calendar") || lowerPath.StartsWith("/api/team-calendars"))
            return "calendar";
        if (lowerPath.StartsWith("/api/context"))
            return "context";
        if (lowerPath.StartsWith("/api/widget"))
            return "widget";
        if (lowerPath.StartsWith("/api/telemetry"))
            return "telemetry";

        return "default";
    }

    /// <summary>
    /// Gets a unique client identifier.
    /// Prefers authenticated user ID, falls back to IP address.
    /// </summary>
    private string GetClientKey(HttpContext context)
    {
        // Use user ID if authenticated
        var userId = context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            return $"user:{userId}";
        }

        // Fall back to IP address
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return $"ip:{ipAddress}";
    }

    /// <summary>
    /// Writes a 429 Too Many Requests response with rate limit details.
    /// </summary>
    private async Task WriteRateLimitResponse(HttpContext context, int limit, int remaining,
        long resetTimestamp, int retryAfterSeconds)
    {
        context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
        context.Response.ContentType = "application/json";

        // Standard rate limit headers
        context.Response.Headers["Retry-After"] = retryAfterSeconds.ToString();
        context.Response.Headers["X-RateLimit-Limit"] = limit.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = remaining.ToString();
        context.Response.Headers["X-RateLimit-Reset"] = resetTimestamp.ToString();

        var response = new
        {
            error = new
            {
                code = "RATE_LIMIT_EXCEEDED",
                message = "Too many requests. Please try again later.",
                retryAfter = retryAfterSeconds
            }
        };

        await context.Response.WriteAsJsonAsync(response);
    }

    /// <summary>
    /// Token bucket implementation for rate limiting with smooth refill.
    /// </summary>
    private class RateLimitBucket
    {
        private readonly int _maxTokens;
        private readonly double _refillRate; // Tokens per second
        private double _tokens;
        private DateTime _lastRefill;
        private readonly object _lock = new();

        public RateLimitBucket(int tokensPerMinute, TimeSpan window)
        {
            _maxTokens = tokensPerMinute;
            _refillRate = tokensPerMinute / window.TotalSeconds;
            _tokens = tokensPerMinute;
            _lastRefill = DateTime.UtcNow;
        }

        /// <summary>
        /// Attempts to consume a token. Returns true if successful, false if rate limited.
        /// </summary>
        public bool TryConsume()
        {
            lock (_lock)
            {
                Refill();

                if (_tokens >= 1.0)
                {
                    _tokens -= 1.0;
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Refills tokens based on elapsed time since last refill.
        /// </summary>
        private void Refill()
        {
            var now = DateTime.UtcNow;
            var elapsed = (now - _lastRefill).TotalSeconds;
            var tokensToAdd = elapsed * _refillRate;

            _tokens = Math.Min(_maxTokens, _tokens + tokensToAdd);
            _lastRefill = now;
        }

        /// <summary>
        /// Gets the number of remaining tokens (rounded down).
        /// </summary>
        public int GetRemainingTokens()
        {
            lock (_lock)
            {
                Refill();
                return (int)Math.Floor(_tokens);
            }
        }

        /// <summary>
        /// Gets the Unix timestamp when the bucket will be fully refilled.
        /// </summary>
        public long GetResetTimestamp()
        {
            lock (_lock)
            {
                Refill();
                var secondsUntilFull = (_maxTokens - _tokens) / _refillRate;
                var resetTime = DateTime.UtcNow.AddSeconds(secondsUntilFull);
                return new DateTimeOffset(resetTime).ToUnixTimeSeconds();
            }
        }

        /// <summary>
        /// Gets the number of seconds until at least one token is available.
        /// </summary>
        public int GetRetryAfterSeconds()
        {
            lock (_lock)
            {
                Refill();
                if (_tokens >= 1.0) return 0;

                var secondsUntilToken = (1.0 - _tokens) / _refillRate;
                return (int)Math.Ceiling(secondsUntilToken);
            }
        }
    }
}
