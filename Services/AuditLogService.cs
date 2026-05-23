using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using System.Security.Claims;

namespace ShiftManager.Services;

/// <summary>
/// Service interface for audit logging
/// </summary>
public interface IAuditLogService
{
    Task LogAsync(string action, string entityType, int? entityId, string description, string? details = null);
    Task LogUserActionAsync(int userId, string action, string entityType, int? entityId, string description, string? details = null);
    /// <summary>Writes an audit entry under an EXPLICIT company (not the current tenant).
    /// Used by cross-company operations like user moves.</summary>
    Task LogUserActionAsync(int userId, int companyId, string action, string entityType, int? entityId, string description, string? details = null);
    Task LogSystemActionAsync(string action, string entityType, int? entityId, string description, string? details = null);
    Task<List<AuditLog>> GetRecentLogsAsync(int count = 10);
}

/// <summary>
/// Service for logging all significant actions in the system for compliance and auditing
/// </summary>
public class AuditLogService : IAuditLogService
{
    private readonly AppDbContext _db;
    private readonly ITenantResolver _tenantResolver;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(
        AppDbContext db,
        ITenantResolver tenantResolver,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AuditLogService> logger)
    {
        _db = db;
        _tenantResolver = tenantResolver;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <summary>
    /// Log an action performed by the current authenticated user
    /// </summary>
    public async Task LogAsync(string action, string entityType, int? entityId, string description, string? details = null)
    {
        try
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext?.User?.Identity?.IsAuthenticated != true)
            {
                _logger.LogWarning("Attempted to log action without authenticated user: {Action}", action);
                return;
            }

            var userId = GetCurrentUserId();
            if (userId == null)
            {
                _logger.LogWarning("Could not determine user ID for audit log: {Action}", action);
                return;
            }

            await LogUserActionAsync(userId.Value, action, entityType, entityId, description, details);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging action {Action} for entity {EntityType}:{EntityId}", action, entityType, entityId);
            // Don't throw - audit logging should never break the main operation
        }
    }

    /// <summary>
    /// Log an action performed by a specific user
    /// </summary>
    public async Task LogUserActionAsync(int userId, string action, string entityType, int? entityId, string description, string? details = null)
    {
        try
        {
            // Fetch user details — still create audit entry even if user was deleted
            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                _logger.LogWarning("User {UserId} not found for audit log — creating log with placeholder", userId);
                user = new AppUser { Id = userId, Email = $"deleted-user-{userId}", DisplayName = $"User #{userId} (deleted)" };
            }

            var httpContext = _httpContextAccessor.HttpContext;
            var ipAddress = httpContext?.Connection?.RemoteIpAddress?.ToString() ?? "Unknown";
            var userAgent = httpContext?.Request?.Headers["User-Agent"].ToString() ?? "Unknown";

            var auditLog = new AuditLog
            {
                CompanyId = _tenantResolver.GetCurrentTenantId(),
                UserId = userId,
                UserEmail = user.Email,
                UserDisplayName = user.DisplayName,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Description = description,
                Details = details,
                Timestamp = DateTime.UtcNow,
                IpAddress = ipAddress,
                UserAgent = userAgent
            };

            _db.AuditLogs.Add(auditLog);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Audit log created: {Action} by {UserEmail} on {EntityType}:{EntityId}",
                action, user.Email, entityType, entityId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging user action {Action} for user {UserId}", action, userId);
            // Don't throw - audit logging should never break the main operation
        }
    }

    /// <summary>
    /// Log an action performed by a specific user under an EXPLICIT company (not the current tenant).
    /// Used by cross-company operations like user moves.
    /// </summary>
    public async Task LogUserActionAsync(int userId, int companyId, string action, string entityType, int? entityId, string description, string? details = null)
    {
        try
        {
            // Fetch user details — still create audit entry even if user was deleted
            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                _logger.LogWarning("User {UserId} not found for audit log — creating log with placeholder", userId);
                user = new AppUser { Id = userId, Email = $"deleted-user-{userId}", DisplayName = $"User #{userId} (deleted)" };
            }

            var httpContext = _httpContextAccessor.HttpContext;
            var ipAddress = httpContext?.Connection?.RemoteIpAddress?.ToString() ?? "Unknown";
            var userAgent = httpContext?.Request?.Headers["User-Agent"].ToString() ?? "Unknown";

            var auditLog = new AuditLog
            {
                CompanyId = companyId,
                UserId = userId,
                UserEmail = user.Email,
                UserDisplayName = user.DisplayName,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Description = description,
                Details = details,
                Timestamp = DateTime.UtcNow,
                IpAddress = ipAddress,
                UserAgent = userAgent
            };

            _db.AuditLogs.Add(auditLog);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Audit log created: {Action} by {UserEmail} on {EntityType}:{EntityId}",
                action, user.Email, entityType, entityId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging user action {Action} for user {UserId}", action, userId);
            // Don't throw - audit logging should never break the main operation
        }
    }

    /// <summary>
    /// Log a system action (not performed by a specific user)
    /// </summary>
    public async Task LogSystemActionAsync(string action, string entityType, int? entityId, string description, string? details = null)
    {
        try
        {
            var auditLog = new AuditLog
            {
                CompanyId = _tenantResolver.GetCurrentTenantId(),
                UserId = null,
                UserEmail = "System",
                UserDisplayName = "System",
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Description = description,
                Details = details,
                Timestamp = DateTime.UtcNow,
                IpAddress = "System",
                UserAgent = "System"
            };

            _db.AuditLogs.Add(auditLog);
            await _db.SaveChangesAsync();

            _logger.LogInformation("System audit log created: {Action} on {EntityType}:{EntityId}",
                action, entityType, entityId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging system action {Action}", action);
            // Don't throw - audit logging should never break the main operation
        }
    }

    /// <summary>
    /// Get recent audit logs for dashboard display.
    /// TENANT-SAFE: AuditLog has a global query filter (CompanyId == currentTenantId) in AppDbContext,
    /// so this query automatically scopes to the current tenant without explicit WHERE.
    /// </summary>
    public async Task<List<AuditLog>> GetRecentLogsAsync(int count = 10)
    {
        try
        {
            return await _db.AuditLogs
                .OrderByDescending(log => log.Timestamp)
                .Take(count)
                .AsNoTracking()
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving recent audit logs");
            return new List<AuditLog>();
        }
    }

    /// <summary>
    /// Get the current user ID from claims
    /// </summary>
    private int? GetCurrentUserId()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.User?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int userId))
        {
            return userId;
        }

        return null;
    }
}
