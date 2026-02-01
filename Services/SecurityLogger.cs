using Microsoft.Extensions.Logging;

namespace ShiftManager.Services;

/// <summary>
/// Implementation of structured security event logging service.
/// Provides consistent, structured logging for security-related events.
/// </summary>
public class SecurityLogger : ISecurityLogger
{
    private readonly ILogger<SecurityLogger> _logger;

    public SecurityLogger(ILogger<SecurityLogger> logger)
    {
        _logger = logger;
    }

    public void LogAuthenticationSuccess(int userId, string email, string role, string ipAddress)
    {
        _logger.LogInformation(
            "SECURITY: Authentication successful | UserId={UserId} Email={Email} Role={Role} IP={IpAddress}",
            userId, email, role, ipAddress);
    }

    public void LogAuthenticationFailure(string email, string ipAddress, string reason)
    {
        _logger.LogWarning(
            "SECURITY: Authentication failed | Email={Email} IP={IpAddress} Reason={Reason}",
            email, ipAddress, reason);
    }

    public void LogAccountLockout(int userId, string email, int failedAttempts, string ipAddress)
    {
        _logger.LogWarning(
            "SECURITY: Account locked | UserId={UserId} Email={Email} FailedAttempts={FailedAttempts} IP={IpAddress}",
            userId, email, failedAttempts, ipAddress);
    }

    public void LogAuthorizationFailure(int userId, string action, string resource, string reason)
    {
        _logger.LogWarning(
            "SECURITY: Authorization denied | UserId={UserId} Action={Action} Resource={Resource} Reason={Reason}",
            userId, action, resource, reason);
    }

    public void LogSecurityThreat(string threatType, string description, string? ipAddress = null, int? userId = null)
    {
        _logger.LogError(
            "SECURITY THREAT: {ThreatType} | Description={Description} IP={IpAddress} UserId={UserId}",
            threatType, description, ipAddress ?? "unknown", userId?.ToString() ?? "anonymous");
    }

    public void LogRateLimitExceeded(string endpoint, string ipAddress, int attemptCount)
    {
        _logger.LogWarning(
            "SECURITY: Rate limit exceeded | Endpoint={Endpoint} IP={IpAddress} Attempts={AttemptCount}",
            endpoint, ipAddress, attemptCount);
    }

    public void LogSensitiveDataAccess(int userId, string dataType, string action, int? targetUserId = null)
    {
        _logger.LogInformation(
            "AUDIT: Sensitive data access | UserId={UserId} DataType={DataType} Action={Action} TargetUserId={TargetUserId}",
            userId, dataType, action, targetUserId?.ToString() ?? "N/A");
    }

    public void LogConfigurationChange(int userId, string configKey, string? oldValue, string? newValue)
    {
        _logger.LogInformation(
            "AUDIT: Configuration changed | UserId={UserId} ConfigKey={ConfigKey} OldValue={OldValue} NewValue={NewValue}",
            userId, configKey, oldValue ?? "null", newValue ?? "null");
    }

    public void LogPermissionChange(int changedBy, int targetUserId, string changeType, string details)
    {
        _logger.LogInformation(
            "AUDIT: Permission changed | ChangedBy={ChangedBy} TargetUserId={TargetUserId} ChangeType={ChangeType} Details={Details}",
            changedBy, targetUserId, changeType, details);
    }
}
