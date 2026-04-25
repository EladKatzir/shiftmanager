using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Pages;
using ShiftManager.Resources;
using System.Security.Claims;

namespace ShiftManager.Pages.My;

[Authorize]
public class NotificationCenterModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<NotificationCenterModel> _logger;

    public NotificationCenterModel(AppDbContext db, ILogger<NotificationCenterModel> logger, IStringLocalizer<SharedResources> localizer) : base(localizer)
    {
        _db = db;
        _logger = logger;
    }

    public List<NotificationViewModel> Notifications { get; set; } = new();
    public int UnreadCount { get; set; }
    // Message / Error properties removed — feedback now flows through TempData → _Layout FeedbackModal bridge.

    public async Task OnGetAsync()
    {
        try
        {
            // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                _logger.LogError("Invalid or missing NameIdentifier claim");
                TempData["ErrorMessage"] = _localizer["Notification_Error_AuthenticationError"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
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
                CssClass = GetNotificationCssClass(n.Type),
                RelatedEntityId = n.RelatedEntityId,
                RelatedEntityType = n.RelatedEntityType,
                DeepLink = GetDeepLink(n.Type, n.RelatedEntityId)
            }).ToList();

            UnreadCount = notifications.Count(n => !n.IsRead);
            _logger.LogInformation("Loaded {Count} notifications for user {UserId}, {UnreadCount} unread", notifications.Count, userId, UnreadCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading notifications");
            TempData["ErrorMessage"] = _localizer["Notification_Error_LoadingFailed"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
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
                return BadRequest(_localizer["Error_InvalidUserClaim"].Value);
            }
            var notification = await _db.UserNotifications
                .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);

            if (notification != null && !notification.IsRead)
            {
                notification.IsRead = true;
                notification.ReadAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                _logger.LogInformation("Marked notification {NotificationId} as read for user {UserId}", id, userId);
                TempData["SuccessMessage"] = _localizer["Notification_MarkedAsRead"].Value;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking notification {NotificationId} as read", id);
            TempData["ErrorMessage"] = _localizer["Notification_Error_UpdateFailed"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
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
                return BadRequest(_localizer["Error_InvalidUserClaim"].Value);
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
                TempData["SuccessMessage"] = _localizer["Notification_MarkedAllAsRead", unreadNotifications.Count].Value;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking all notifications as read");
            TempData["ErrorMessage"] = _localizer["Notification_Error_MarkAllFailed"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
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
                return BadRequest(_localizer["Error_InvalidUserClaim"].Value);
            }
            var notification = await _db.UserNotifications
                .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);

            if (notification != null)
            {
                _db.UserNotifications.Remove(notification);
                await _db.SaveChangesAsync();

                _logger.LogInformation("Deleted notification {NotificationId} for user {UserId}", id, userId);
                TempData["SuccessMessage"] = _localizer["Notification_Deleted"].Value;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting notification {NotificationId}", id);
            TempData["ErrorMessage"] = _localizer["Notification_Error_DeleteFailed"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
        }

        return RedirectToPage();
    }

    // Returns a Lucide icon name (see IconTagHelper). The view renders via <icon>
    // with a regex-gated fallback, so legacy stored values still display.
    private static string GetNotificationIcon(NotificationType type) => type switch
    {
        NotificationType.ShiftAdded => "calendar",
        NotificationType.ShiftRemoved => "trash-2",
        NotificationType.TimeOffApproved => "check",
        NotificationType.TimeOffDeclined => "x",
        NotificationType.SwapRequestApproved => "refresh-cw",
        NotificationType.SwapRequestDeclined => "x-circle",
        _ => "megaphone"
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

    private static string? GetDeepLink(NotificationType type, int? entityId)
    {
        if (!entityId.HasValue) return null;

        return type switch
        {
            NotificationType.SwapRequestApproved or NotificationType.SwapRequestDeclined
                => $"/Requests?tab=swap&highlight={entityId.Value}",
            NotificationType.TimeOffApproved or NotificationType.TimeOffDeclined
                => $"/Requests?tab=timeoff&highlight={entityId.Value}",
            _ => null
        };
    }

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
        public int? RelatedEntityId { get; set; }
        public string? RelatedEntityType { get; set; }
        public string? DeepLink { get; set; }
    }
}