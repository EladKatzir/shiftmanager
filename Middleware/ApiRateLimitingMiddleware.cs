using System.Collections.Concurrent;
using ShiftManager.Models.Api;

namespace ShiftManager.Middleware;

/// <summary>
/// Rate limits API requests based on API key configuration.
/// Uses token bucket algorithm for smooth rate limiting.
/// </summary>
public class ApiRateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiRateLimitingMiddleware> _logger;

    // In-memory rate limit tracking (use Redis for multi-instance deployments)
    private static readonly ConcurrentDictionary<int, RateLimitBucket> _buckets = new();

    public ApiRateLimitingMiddleware(RequestDelegate next, ILogger<ApiRateLimitingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only process API routes
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        // Get API key from context (set by ApiAuthenticationMiddleware)
        if (context.Items["ApiKey"] is not ApiKey apiKey)
        {
            // No API key found - authentication middleware should have rejected this already
            await _next(context);
            return;
        }

        // Get or create rate limit bucket for this API key
        var bucket = _buckets.GetOrAdd(apiKey.Id, _ => new RateLimitBucket(apiKey.RateLimitPerMinute));

        // Check if request is allowed
        if (!bucket.TryConsume())
        {
            var retryAfter = bucket.GetRetryAfterSeconds();
            _logger.LogWarning("Rate limit exceeded for API key {KeyId} (CompanyId: {CompanyId}). Retry after {RetryAfter}s",
                apiKey.Id, apiKey.CompanyId, retryAfter);

            await WriteRateLimitResponse(context, retryAfter);
            return;
        }

        // Add rate limit headers to response
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-RateLimit-Limit"] = apiKey.RateLimitPerMinute.ToString();
            context.Response.Headers["X-RateLimit-Remaining"] = bucket.GetRemainingTokens().ToString();
            context.Response.Headers["X-RateLimit-Reset"] = bucket.GetResetTimestamp().ToString();
            return Task.CompletedTask;
        });

        await _next(context);
    }

    /// <summary>
    /// Writes a 429 Too Many Requests response
    /// </summary>
    private async Task WriteRateLimitResponse(HttpContext context, int retryAfterSeconds)
    {
        context.Response.StatusCode = 429;
        context.Response.ContentType = "application/problem+json";
        context.Response.Headers["Retry-After"] = retryAfterSeconds.ToString();

        var problem = ApiProblemDetails.RateLimitExceeded(retryAfterSeconds, context.Request.Path);
        await context.Response.WriteAsJsonAsync(problem);
    }

    /// <summary>
    /// Token bucket implementation for rate limiting
    /// </summary>
    private class RateLimitBucket
    {
        private readonly int _maxTokens;
        private readonly double _refillRate; // Tokens per second
        private double _tokens;
        private DateTime _lastRefill;
        private readonly object _lock = new();

        public RateLimitBucket(int tokensPerMinute)
        {
            _maxTokens = tokensPerMinute;
            _refillRate = tokensPerMinute / 60.0; // Convert to tokens per second
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
        /// Refills tokens based on elapsed time
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
        /// Gets the number of remaining tokens (rounded down)
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
        /// Gets the Unix timestamp when the bucket will be fully refilled
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
        /// Gets the number of seconds until at least one token is available
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
