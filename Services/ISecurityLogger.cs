namespace ShiftManager.Services;

/// <summary>
/// Service for structured security event logging with consistent formatting.
/// </summary>
public interface ISecurityLogger
{
    /// <summary>
    /// Logs a successful authentication event.
    /// </summary>
    void LogAuthenticationSuccess(int userId, string email, string role, string ipAddress);

    /// <summary>
    /// Logs a failed authentication attempt.
    /// </summary>
    void LogAuthenticationFailure(string email, string ipAddress, string reason);

    /// <summary>
    /// Logs an account lockout event.
    /// </summary>
    void LogAccountLockout(int userId, string email, int failedAttempts, string ipAddress);

    /// <summary>
    /// Logs an authorization failure (access denied).
    /// </summary>
    void LogAuthorizationFailure(int userId, string action, string resource, string reason);

    /// <summary>
    /// Logs a potential security threat or suspicious activity.
    /// </summary>
    void LogSecurityThreat(string threatType, string description, string? ipAddress = null, int? userId = null);

    /// <summary>
    /// Logs a rate limit exceeded event.
    /// </summary>
    void LogRateLimitExceeded(string endpoint, string ipAddress, int attemptCount);

    /// <summary>
    /// Logs a sensitive data access event (audit trail).
    /// </summary>
    void LogSensitiveDataAccess(int userId, string dataType, string action, int? targetUserId = null);

    /// <summary>
    /// Logs a configuration change event.
    /// </summary>
    void LogConfigurationChange(int userId, string configKey, string? oldValue, string? newValue);

    /// <summary>
    /// Logs a permission change event.
    /// </summary>
    void LogPermissionChange(int changedBy, int targetUserId, string changeType, string details);
}
