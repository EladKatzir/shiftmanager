# Rate Limiting for UI Endpoints (B-027)

This document describes the rate limiting implementation for ShiftManager UI API endpoints.

## Overview

Rate limiting protects the application from excessive requests and ensures fair usage across all users. The implementation uses a token bucket algorithm for smooth rate limiting with automatic recovery.

## Rate Limits

| Endpoint Category | Limit | Window | Notes |
|-------------------|-------|--------|-------|
| Calendar API | 60 requests | 1 minute | Higher limit for frequently accessed calendar views |
| Context Switcher API | 30 requests | 1 minute | Standard limit for UI context changes |
| Widget API | 30 requests | 1 minute | Standard limit for widget data |
| Telemetry API | 30 requests | 1 minute | Standard limit for analytics data |

### Endpoint Paths

The following path prefixes are rate limited:

- `/api/calendar/*` - Calendar data endpoints
- `/Api/Calendar/*` - Calendar Razor Page endpoints
- `/api/team-calendars/*` - Team calendar REST API
- `/api/context/*` - Context switcher endpoints
- `/api/widget/*` - Widget data endpoints
- `/Api/Telemetry/*` - Client telemetry endpoints

## Response Headers

All rate-limited responses include these headers:

| Header | Description |
|--------|-------------|
| `X-RateLimit-Limit` | Maximum requests allowed per window |
| `X-RateLimit-Remaining` | Requests remaining in current window |
| `X-RateLimit-Reset` | Unix timestamp when the limit resets |

## Rate Limit Exceeded Response

When a client exceeds the rate limit, they receive:

- **HTTP Status**: `429 Too Many Requests`
- **Retry-After Header**: Seconds to wait before retrying

**Response Body:**
```json
{
    "error": {
        "code": "RATE_LIMIT_EXCEEDED",
        "message": "Too many requests. Please try again later.",
        "retryAfter": 5
    }
}
```

## Client Identification

Rate limits are tracked per-user using:

1. **Authenticated users**: User ID from claims
2. **Anonymous users**: IP address

This ensures authenticated users get their own quota while preventing IP-based abuse.

## Client-Side Handling

The `ApiClient` JavaScript module (`/js/api-client.js`) automatically handles rate limiting:

### Automatic Retry

When a 429 response is received:
1. Parse the `Retry-After` header
2. Wait the specified duration (or use exponential backoff)
3. Retry the request (up to 3 times)

### Usage Example

```javascript
// Use ApiClient instead of raw fetch for automatic rate limit handling
const response = await ApiClient.get('/api/calendar/data');
const data = await response.json();

// POST with JSON body
await ApiClient.post('/api/widget/update', { setting: 'value' });

// Check current rate limit status
const limitInfo = ApiClient.getRateLimitInfo('/api/calendar');
console.log(`Requests remaining: ${limitInfo?.remaining}`);
```

### Events

The ApiClient dispatches events for rate limit situations:

```javascript
window.addEventListener('api:ratelimit', (e) => {
    console.log('Rate limited:', e.detail);
    // e.detail = {
    //     url: '/api/calendar/...',
    //     endpoint: 'calendar',
    //     delayMs: 5000,
    //     retryAttempt: 1,
    //     isFinalFailure: false
    // }
});
```

## Architecture

### Middleware Stack

```
Request
    |
    v
[RateLimitingMiddleware]  <-- B-027: UI rate limiting (user/IP based)
    |
    v
[ApiExceptionMiddleware]   <-- B-028: Standardized errors
    |
    v
[ApiRequestLoggingMiddleware]
    |
    v
[ApiAuthenticationMiddleware]
    |
    v
[ApiRateLimitingMiddleware] <-- Existing: API key rate limiting
    |
    v
Controllers / Razor Pages
```

### Token Bucket Algorithm

The implementation uses a token bucket algorithm:

1. Each client has a bucket with max tokens equal to the rate limit
2. Tokens refill continuously based on the window duration
3. Each request consumes one token
4. If no tokens available, request is rejected with 429

This provides:
- **Burst tolerance**: Full quota available immediately
- **Smooth refill**: Gradual recovery rather than sudden reset
- **Fair distribution**: Each client gets independent quota

### Memory Cache

Rate limit state is stored in `IMemoryCache`:
- Keys: `ratelimit:ui:{endpoint}:{clientKey}`
- Expiry: Matches the rate limit window (1 minute)

For multi-instance deployments, consider using Redis for distributed rate limiting.

## Configuration

Rate limits are configured in `RateLimitingMiddleware.cs`:

```csharp
private static readonly Dictionary<string, (int Limit, TimeSpan Window)> EndpointLimits = new()
{
    { "/api/calendar", (60, TimeSpan.FromMinutes(1)) },
    { "/api/context", (30, TimeSpan.FromMinutes(1)) },
    { "/api/widget", (30, TimeSpan.FromMinutes(1)) },
    // ...
};
```

## Monitoring

Rate limit events are logged at Warning level:

```
[Warning] UI rate limit exceeded for /api/calendar/month by user:123. Retry after 5s
```

Search for these logs to identify:
- Users hitting limits frequently (may indicate automation or bugs)
- Endpoints with high usage (may need limit adjustment)

## Testing

### Manual Testing

1. Open browser DevTools Network tab
2. Rapidly refresh a calendar page
3. Observe `X-RateLimit-Remaining` decreasing in response headers
4. When limit exceeded, observe 429 response with `Retry-After` header
5. Client should automatically retry after the specified duration

### Automated Testing

```csharp
[Fact]
public async Task Calendar_ShouldReturnRateLimitHeaders()
{
    var response = await _client.GetAsync("/api/calendar/month");

    Assert.True(response.Headers.Contains("X-RateLimit-Limit"));
    Assert.True(response.Headers.Contains("X-RateLimit-Remaining"));
    Assert.True(response.Headers.Contains("X-RateLimit-Reset"));
}

[Fact]
public async Task Calendar_ShouldReturn429WhenLimitExceeded()
{
    // Make 61 requests rapidly (limit is 60)
    for (int i = 0; i < 61; i++)
    {
        var response = await _client.GetAsync("/api/calendar/month");
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            Assert.True(response.Headers.Contains("Retry-After"));
            return;
        }
    }
    Assert.Fail("Rate limit was not enforced");
}
```

## Related Documentation

- [API Documentation](./API_DOCUMENTATION.md) - Full API reference
- [Security Hardening](./SECURITY_HARDENING_COMPLETE.md) - Security measures
- [Client Telemetry](./genesis/21-V3-ORGANIZATIONAL-HIERARCHY.md) - Telemetry endpoints
