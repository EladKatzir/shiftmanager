using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models.Api.Dto;
using ShiftManager.Models.Support;

namespace ShiftManager.Services.Api;

/// <summary>
/// Wrapper service for Notification API operations.
/// Isolates API logic from existing notification services to maintain zero regression.
/// </summary>
public class NotificationApiService
{
    private readonly AppDbContext _context;
    private readonly ILogger<NotificationApiService> _logger;

    public NotificationApiService(AppDbContext context, ILogger<NotificationApiService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Lists notifications with pagination and filtering.
    /// Respects global query filters for multi-tenant isolation.
    /// </summary>
    public async Task<(List<NotificationDto> Notifications, int TotalCount)> ListNotificationsAsync(
        int companyId,
        int page = 1,
        int pageSize = 50,
        int? userId = null,
        bool? isRead = null,
        string? type = null)
    {
        // Validate pagination parameters
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 100) pageSize = 100; // Max page size

        var query = _context.UserNotifications.AsQueryable();

        // Manual CompanyId filter
        query = query.Where(n => n.CompanyId == companyId);

        // Apply user filter
        if (userId.HasValue)
        {
            query = query.Where(n => n.UserId == userId.Value);
        }

        // Apply read status filter
        if (isRead.HasValue)
        {
            query = query.Where(n => n.IsRead == isRead.Value);
        }

        // Apply type filter
        if (!string.IsNullOrEmpty(type))
        {
            if (Enum.TryParse<NotificationType>(type, true, out var typeEnum))
            {
                query = query.Where(n => n.Type == typeEnum);
            }
        }

        // Get total count before pagination
        var totalCount = await query.CountAsync();

        // Apply pagination
        var notifications = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Map to DTOs
        var dtos = notifications.Select(n => NotificationDto.FromEntity(n)).ToList();

        return (dtos, totalCount);
    }

    /// <summary>
    /// Gets a single notification by ID.
    /// </summary>
    public async Task<NotificationDto?> GetNotificationAsync(int companyId, int notificationId)
    {
        var notification = await _context.UserNotifications
            .Where(n => n.CompanyId == companyId && n.Id == notificationId)
            .FirstOrDefaultAsync();

        if (notification == null)
        {
            return null;
        }

        return NotificationDto.FromEntity(notification);
    }

    /// <summary>
    /// Marks a notification as read.
    /// </summary>
    public async Task<(NotificationDto? Notification, string? Error)> MarkAsReadAsync(
        int companyId,
        int notificationId,
        int? userId = null)
    {
        var query = _context.UserNotifications
            .Where(n => n.CompanyId == companyId && n.Id == notificationId);

        // Optionally filter by user ID for security
        if (userId.HasValue)
        {
            query = query.Where(n => n.UserId == userId.Value);
        }

        var notification = await query.FirstOrDefaultAsync();

        if (notification == null)
        {
            return (null, $"Notification {notificationId} not found");
        }

        if (notification.IsRead)
        {
            // Already read, return current state
            return (NotificationDto.FromEntity(notification), null);
        }

        notification.IsRead = true;
        notification.ReadAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Notification marked as read via API: NotificationId={NotificationId}, UserId={UserId}",
            notificationId, notification.UserId);

        return (NotificationDto.FromEntity(notification), null);
    }

    /// <summary>
    /// Marks all notifications for a user as read.
    /// </summary>
    public async Task<int> MarkAllAsReadAsync(int companyId, int userId)
    {
        var unreadNotifications = await _context.UserNotifications
            .Where(n => n.CompanyId == companyId && n.UserId == userId && !n.IsRead)
            .ToListAsync();

        foreach (var notification in unreadNotifications)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("All notifications marked as read via API: UserId={UserId}, Count={Count}",
            userId, unreadNotifications.Count);

        return unreadNotifications.Count;
    }
}
