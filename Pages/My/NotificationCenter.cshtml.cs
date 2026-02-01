using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using System.Security.Claims;

namespace ShiftManager.Pages.My;

[Authorize]
public class NotificationCenterModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<NotificationCenterModel> _logger;

    public NotificationCenterModel(AppDbContext db, ILogger<NotificationCenterModel> logger)
    {
        _db = db;
        _logger = logger;
    }

    public List<NotificationViewModel> Notifications { get; set; } = new();
    public int UnreadCount { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }

    public async Task OnGetAsync()
    {
        try
        {
            // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                _logger.LogError("Invalid or missing NameIdentifier claim");
                Error = "Authentication error. Please log in again.";
                return;
            }
            _logger.LogInformation("Loading notifications for user {UserId}", userId);

            var notifications = await _db.UserNotifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(50) // Limit to recent 50 notifications
                .ToListAsync();

            Notifications = notifications.Select(n => new NotificationViewModel
            {
                Id = n.Id,
                Type = n.Type,
                Title = n.Title,
                Message = n.Message,
                IsRead = n.IsRead,
                CreatedAt = n.CreatedAt,
                ReadAt = n.ReadAt,
                Icon = GetNotificationIcon(n.Type),
                CssClass = GetNotificationCssClass(n.Type)
            }).ToList();

            UnreadCount = notifications.Count(n => !n.IsRead);
            _logger.LogInformation("Loaded {Count} notifications for user {UserId}, {UnreadCount} unread", notifications.Count, userId, UnreadCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading notifications");
            Error = "An error occurred while loading notifications. Please try again.";
        }
    }

    public async Task<IActionResult> OnPostMarkAsReadAsync(int id)
    {
        try
        {
            // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                _logger.LogError("Invalid or missing NameIdentifier claim");
                return BadRequest("Invalid user claim");
            }
            var notification = await _db.UserNotifications
                .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);

            if (notification != null && !notification.IsRead)
            {
                notification.IsRead = true;
                notification.ReadAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                _logger.LogInformation("Marked notification {NotificationId} as read for user {UserId}", id, userId);
                Message = "Notification marked as read.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking notification {NotificationId} as read", id);
            Error = "An error occurred while updating the notification.";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostMarkAllAsReadAsync()
    {
        try
        {
            // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                _logger.LogError("Invalid or missing NameIdentifier claim");
                return BadRequest("Invalid user claim");
            }
            var unreadNotifications = await _db.UserNotifications
                .Where(n => n.UserId == userId && !n.IsRead)
                .ToListAsync();

            if (unreadNotifications.Any())
            {
                var now = DateTime.UtcNow;
                foreach (var notification in unreadNotifications)
                {
                    notification.IsRead = true;
                    notification.ReadAt = now;
                }

                await _db.SaveChangesAsync();
                _logger.LogInformation("Marked {Count} notifications as read for user {UserId}", unreadNotifications.Count, userId);
                Message = $"Marked {unreadNotifications.Count} notifications as read.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking all notifications as read");
            Error = "An error occurred while updating notifications.";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        try
        {
            // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                _logger.LogError("Invalid or missing NameIdentifier claim");
                return BadRequest("Invalid user claim");
            }
            var notification = await _db.UserNotifications
                .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);

            if (notification != null)
            {
                _db.UserNotifications.Remove(notification);
                await _db.SaveChangesAsync();

                _logger.LogInformation("Deleted notification {NotificationId} for user {UserId}", id, userId);
                Message = "Notification deleted.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting notification {NotificationId}", id);
            Error = "An error occurred while deleting the notification.";
        }

        return RedirectToPage();
    }

    private static string GetNotificationIcon(NotificationType type) => type switch
    {
        NotificationType.ShiftAdded => "📅",
        NotificationType.ShiftRemoved => "🗑️",
        NotificationType.TimeOffApproved => "✅",
        NotificationType.TimeOffDeclined => "❌",
        NotificationType.SwapRequestApproved => "🔄",
        NotificationType.SwapRequestDeclined => "⛔",
        _ => "📢"
    };

    private static string GetNotificationCssClass(NotificationType type) => type switch
    {
        NotificationType.ShiftAdded => "notification-shift-added",
        NotificationType.ShiftRemoved => "notification-shift-removed",
        NotificationType.TimeOffApproved => "notification-approved",
        NotificationType.TimeOffDeclined => "notification-declined",
        NotificationType.SwapRequestApproved => "notification-approved",
        NotificationType.SwapRequestDeclined => "notification-declined",
        _ => "notification-default"
    };

    public class NotificationViewModel
    {
        public int Id { get; set; }
        public NotificationType Type { get; set; }
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ReadAt { get; set; }
        public string Icon { get; set; } = "";
        public string CssClass { get; set; } = "";
    }
}