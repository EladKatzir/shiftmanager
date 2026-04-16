using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Services;
using System.Security.Claims;
using System.Text.Json;

namespace ShiftManager.Pages.Api.Calendar;

/// <summary>
/// ✅ PHASE 20: API endpoint to delete on-duty assignments from calendar views
/// </summary>
// 2026-04-15: widened from [Authorize(Policy = "Grant:ManageOnDuty")] to plain [Authorize]
// so EditOnCallCalendar-grant holders can reach the handler. In-handler
// CanUserManageOnDutyAsync is the auth gate.
[Authorize]
[IgnoreAntiforgeryToken]
public class DeleteOnDutyModel : PageModel
{
    private readonly IOnDutyService _onDutyService;
    private readonly INotificationService _notificationService;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<DeleteOnDutyModel> _logger;

    public DeleteOnDutyModel(
        IOnDutyService onDutyService,
        INotificationService notificationService,
        IAuditLogService auditLogService,
        ILogger<DeleteOnDutyModel> logger)
    {
        _onDutyService = onDutyService;
        _notificationService = notificationService;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            // Log request start
            _logger.LogInformation("DeleteOnDuty: Request received from {UserAgent}, Auth={IsAuth}",
                Request.Headers["User-Agent"].ToString(),
                User.Identity?.IsAuthenticated ?? false);

            // Parse JSON body
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            var data = JsonSerializer.Deserialize<DeleteOnDutyRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            // Validate input
            if (data == null || data.Id <= 0)
            {
                return new JsonResult(new { success = false, message = "Invalid on-duty ID" })
                {
                    StatusCode = 400
                };
            }

            // Get current user
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var currentUserId))
            {
                _logger.LogWarning("DeleteOnDuty: User not authenticated - IsAuth={IsAuth}, NameId={NameId}",
                    User.Identity?.IsAuthenticated ?? false,
                    userIdClaim ?? "null");
                return new JsonResult(new { success = false, message = "User not authenticated" })
                {
                    StatusCode = 401
                };
            }

            // Check permissions
            if (!await _onDutyService.CanUserManageOnDutyAsync(currentUserId))
            {
                _logger.LogWarning("SECURITY: User {UserId} attempted to delete on-duty {OnDutyId} without permission",
                    currentUserId, data.Id);
                return new JsonResult(new { success = false, message = "You do not have permission to delete on-duty assignments" })
                {
                    StatusCode = 403
                };
            }

            // Get the on-duty before deleting
            var onDuty = await _onDutyService.GetOnDutyByIdAsync(data.Id);
            if (onDuty == null)
            {
                return new JsonResult(new { success = false, message = "On-duty assignment not found" })
                {
                    StatusCode = 404
                };
            }

            // Delete the on-duty assignment
            var result = await _onDutyService.CancelOnDutyAsync(data.Id);

            if (!result.Success)
            {
                return new JsonResult(new { success = false, message = result.Message })
                {
                    StatusCode = 400
                };
            }

            // Notify user and audit log
            await _notificationService.CreateOnDutyCanceledNotificationAsync(
                userId: onDuty.UserId,
                onDutyType: onDuty.Type,
                onDutyDate: onDuty.Date,
                onDutyId: data.Id);

            await _auditLogService.LogAsync(
                action: "OnDutyDeletedQuick",
                entityType: "OnDuty",
                entityId: data.Id,
                description: $"Deleted on-duty {onDuty.Type} for user {onDuty.UserId} on {onDuty.Date:yyyy-MM-dd} via calendar quick-delete",
                details: JsonSerializer.Serialize(new
                {
                    OnDutyId = data.Id,
                    OnDutyType = onDuty.Type,
                    UserId = onDuty.UserId,
                    OnDutyDate = onDuty.Date,
                    Source = "CalendarQuickDelete"
                }));

            _logger.LogInformation("On-duty deleted via quick-delete: OnDutyId={OnDutyId}, UserId={UserId}",
                data.Id, currentUserId);

            return new JsonResult(new
            {
                success = true,
                message = "On-duty assignment deleted successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting on-duty via quick-delete");
            return new JsonResult(new { success = false, message = "An error occurred while deleting the on-duty assignment" })
            {
                StatusCode = 500
            };
        }
    }

    private class DeleteOnDutyRequest
    {
        public int Id { get; set; }
    }
}
