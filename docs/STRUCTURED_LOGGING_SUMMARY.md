# Structured Logging Improvements

**Date**: 2025-10-27
**Status**: ✅ COMPLETED - Structured Logging Services Implemented

## Overview

Implemented comprehensive structured logging infrastructure to improve security monitoring, performance tracking, and audit trail capabilities across the ShiftManager application.

## Components Added

### 1. SecurityLogger Service

**File**: `Services/ISecurityLogger.cs` & `Services/SecurityLogger.cs`

A dedicated service for consistent security event logging with structured data.

#### Methods Implemented:

```csharp
void LogAuthenticationSuccess(int userId, string email, string role, string ipAddress)
void LogAuthenticationFailure(string email, string ipAddress, string reason)
void LogAccountLockout(int userId, string email, int failedAttempts, string ipAddress)
void LogAuthorizationFailure(int userId, string action, string resource, string reason)
void LogSecurityThreat(string threatType, string description, string? ipAddress, int? userId)
void LogRateLimitExceeded(string endpoint, string ipAddress, int attemptCount)
void LogSensitiveDataAccess(int userId, string dataType, string action, int? targetUserId)
void LogConfigurationChange(int userId, string configKey, string? oldValue, string? newValue)
void LogPermissionChange(int changedBy, int targetUserId, string changeType, string details)
```

#### Benefits:
- ✅ **Consistent Format**: All security logs follow the same structure
- ✅ **Structured Data**: Uses named parameters for easy parsing and searching
- ✅ **Appropriate Log Levels**: Information for success, Warning for failures, Error for threats
- ✅ **Audit Trail**: Complete record of security-relevant events
- ✅ **SIEM Compatible**: Structured logs easily integrate with security monitoring tools

#### Example Usage:

```csharp
// In Login.cshtml.cs
_securityLogger.LogAuthenticationSuccess(user.Id, user.Email, user.Role.ToString(), ipAddress);

// In authorization checks
_securityLogger.LogAuthorizationFailure(
    currentUserId,
    "ApproveRequest",
    $"TimeOffRequest:{requestId}",
    "User not authorized for this company");

// In rate limiting
_securityLogger.LogRateLimitExceeded("/Auth/Login", ipAddress, attemptCount);
```

### 2. RequestLoggingMiddleware

**File**: `Middleware/RequestLoggingMiddleware.cs`

HTTP request logging middleware with performance metrics and correlation tracking.

#### Features:

1. **Correlation IDs**
   - Generates unique 12-character correlation ID for each request
   - Stored in `HttpContext.Items["CorrelationId"]`
   - Enables request tracing across distributed logs

2. **Performance Metrics**
   - Tracks request duration using `Stopwatch`
   - Logs slow requests (>1 second) with WARNING level
   - Helps identify performance bottlenecks

3. **Request/Response Logging**
   ```
   REQUEST START | CorrelationId=abc123def456 Method=POST Path=/Auth/Login UserId=anonymous IP=192.168.1.1
   REQUEST END | CorrelationId=abc123def456 Method=POST Path=/Auth/Login StatusCode=200 Duration=245ms UserId=5
   ```

4. **Error Logging**
   - Captures exceptions with full context
   - Includes correlation ID, duration, user info
   - Preserves stack trace while adding structured data

5. **Status Code Based Log Levels**
   - 200-399: Information
   - 400-499: Warning (client errors)
   - 500-599: Error (server errors)

#### Example Log Output:

```
INFO: REQUEST START | CorrelationId=a1b2c3d4e5f6 Method=GET Path=/Admin/Users UserId=42 IP=10.0.0.5
INFO: REQUEST END | CorrelationId=a1b2c3d4e5f6 Method=GET Path=/Admin/Users StatusCode=200 Duration=156ms UserId=42
WARN: PERFORMANCE: Slow request | CorrelationId=x9y8z7w6v5u4 Path=/Admin/Analytics Duration=1523ms
ERROR: REQUEST ERROR | CorrelationId=m1n2o3p4q5r6 Method=POST Path=/Requests/TimeOff/Create Duration=89ms UserId=15 Error=Database timeout
```

## Integration with Application

### Service Registration (Program.cs)

```csharp
// Added to dependency injection container
builder.Services.AddScoped<ISecurityLogger, SecurityLogger>();
```

### Middleware Pipeline (Program.cs)

```csharp
app.UseStaticFiles();
app.UseRouting();

// Request logging middleware (after routing, before authentication)
app.UseRequestLogging();

// Security headers middleware
app.Use(async (context, next) => { ... });

// Localization middleware
app.UseRequestLocalization();

// Authentication/Authorization
app.UseAuthentication();
app.UseAuthorization();
```

**Placement Rationale**:
- After `UseRouting()`: Route information is available for logging
- Before `UseAuthentication()`: Can log both authenticated and anonymous requests
- Before custom middleware: Wraps all downstream processing

## Benefits

### 1. Security Monitoring

**Before**:
```csharp
_logger.LogWarning("User {UserId} attempted to approve request without permission", userId);
```

**After (Structured)**:
```csharp
_securityLogger.LogAuthorizationFailure(
    userId,
    "ApproveTimeOffRequest",
    $"TimeOffRequest:{requestId}",
    "User does not have access to target company");
```

**Improvement**: Consistent format, searchable fields, clear security context

### 2. Performance Tracking

**Before**: No automated performance logging

**After**:
- All requests logged with duration
- Automatic slow request detection (>1s)
- Correlation IDs for distributed tracing
- Easy identification of performance hotspots

**Example Query**: "Show all requests taking >1 second in the last 24 hours"

### 3. Audit Trail

**Security events tracked**:
- ✅ Authentication successes/failures
- ✅ Account lockouts
- ✅ Authorization denials
- ✅ Permission changes
- ✅ Configuration modifications
- ✅ Sensitive data access
- ✅ Rate limit violations

**Compliance**: Provides audit trail for security compliance (SOC 2, ISO 27001, GDPR)

### 4. Troubleshooting

**Correlation IDs** enable:
- Tracing single request through entire stack
- Finding all log entries related to specific user action
- Debugging distributed operations
- Identifying root cause of errors

**Example**:
```
User reports: "My time-off request failed"
Developer: Searches logs for user ID, finds correlation ID
Developer: Filters all logs by correlation ID
Developer: Sees full request lifecycle, identifies validation failure at line X
```

## Usage Guidelines

### When to Use SecurityLogger

✅ **DO use for**:
- Authentication events (login, logout, password reset)
- Authorization failures (access denied)
- Permission changes (role updates, user management)
- Configuration changes
- Sensitive data access (viewing other users' data)
- Security threats (rate limit exceeded, invalid input attacks)

❌ **DON'T use for**:
- Normal business operations (creating shifts, approving requests - use regular logger)
- Debug information (use Debug log level with regular logger)
- Performance metrics (RequestLoggingMiddleware handles this)

### When to Use Regular ILogger

✅ **DO use for**:
- Business logic flow (request approved, shift created)
- Debug information during development
- Application lifecycle events (startup, shutdown)
- General informational messages

### Log Levels Guidelines

- **Debug**: Development/troubleshooting info (not logged in production)
- **Information**: Normal operations, business events
- **Warning**: Unexpected but handled situations (rate limits, validation failures)
- **Error**: Exceptions, failures that need attention
- **Critical**: System-level failures requiring immediate action

## Example Implementations

### Authentication with Security Logging

```csharp
// Pages/Auth/Login.cshtml.cs
private readonly ISecurityLogger _securityLogger;

public async Task<IActionResult> OnPostAsync()
{
    var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    // ... authentication logic ...

    if (user == null || !PasswordHasher.Verify(Password, user.PasswordHash, user.PasswordSalt))
    {
        _securityLogger.LogAuthenticationFailure(Email, ipAddress, "Invalid credentials");

        if (user != null)
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= 5)
            {
                _securityLogger.LogAccountLockout(
                    user.Id, user.Email, user.FailedLoginAttempts, ipAddress);
                user.LockoutEnd = DateTime.UtcNow.AddMinutes(15);
            }
        }
        return Page();
    }

    _securityLogger.LogAuthenticationSuccess(
        user.Id, user.Email, user.Role.ToString(), ipAddress);

    // ... create authentication cookie ...
}
```

### Authorization with Security Logging

```csharp
// Pages/Requests/Index.cshtml.cs
private readonly ISecurityLogger _securityLogger;

public async Task<IActionResult> OnPostApproveTimeOffAsync(int id)
{
    var request = await _db.TimeOffRequests.FindAsync(id);
    var currentUser = await _db.Users.FindAsync(currentUserId);

    if (!await HasAccessToCompany(currentUser, request.CompanyId))
    {
        _securityLogger.LogAuthorizationFailure(
            currentUserId,
            "ApproveTimeOffRequest",
            $"TimeOffRequest:{id} Company:{request.CompanyId}",
            "User does not manage this company");

        return Forbid();
    }

    // ... approve request ...
}
```

### Configuration Changes with Audit Logging

```csharp
// Pages/Admin/Config.cshtml.cs
private readonly ISecurityLogger _securityLogger;

public async Task<IActionResult> OnPostAsync()
{
    var currentUserId = GetCurrentUserId();
    var oldRestHours = GetInt(companyId, "RestHours", 8);

    await Set(companyId, "RestHours", RestHours.ToString());

    _securityLogger.LogConfigurationChange(
        currentUserId,
        "RestHours",
        oldRestHours.ToString(),
        RestHours.ToString());

    // ... continue ...
}
```

## Log Analysis Examples

### Finding Failed Login Attempts
```
grep "SECURITY: Authentication failed" application.log | grep "2025-10-27"
```

### Finding Slow Requests
```
grep "PERFORMANCE: Slow request" application.log | grep "Duration=[0-9]{4,}ms"
```

### Tracing Specific Request
```
grep "CorrelationId=abc123def456" application.log
```

### Security Threats by IP
```
grep "SECURITY THREAT" application.log | grep "IP=192.168.1.100"
```

## Future Enhancements

### Potential Improvements:

1. **Structured Logging Sinks**
   - Add Serilog for rich structured logging
   - Send logs to Elasticsearch/Splunk
   - Enable complex queries and dashboards

2. **Application Insights Integration**
   - Send logs to Azure Application Insights
   - Get automatic dashboards and alerts
   - Distributed tracing across services

3. **Log Enrichment**
   - Add machine name, process ID
   - Include build version, environment
   - Add business context (company ID, tenant)

4. **Alerts and Notifications**
   - Alert on multiple failed logins
   - Notify on security threats
   - Dashboard for security metrics

5. **Log Retention Policies**
   - Archive old logs
   - Compress historical data
   - Compliance-driven retention

## Conclusion

✅ **Structured logging infrastructure successfully implemented**

**Achievements**:
- Consistent security event logging
- Automated request/response tracking with performance metrics
- Correlation IDs for request tracing
- Comprehensive audit trail
- SIEM-compatible log format

**Benefits**:
- Improved security monitoring and incident response
- Faster troubleshooting with correlation IDs
- Performance bottleneck identification
- Compliance-ready audit trails
- Professional, searchable log format

The application now has enterprise-grade logging capabilities suitable for production security monitoring and compliance requirements.

## Commits

All logging improvements committed with comprehensive documentation:
- SecurityLogger service implementation
- RequestLoggingMiddleware with correlation IDs
- Service registration and middleware configuration
- Usage guidelines and examples
