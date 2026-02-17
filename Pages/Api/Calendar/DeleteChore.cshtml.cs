using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Services;
using System.Security.Claims;
using System.Text.Json;

namespace ShiftManager.Pages.Api.Calendar;

/// <summary>
/// ✅ PHASE 20: API endpoint to delete chores from calendar views
/// </summary>
[Authorize(Policy = "Grant:AssignChores")]
[IgnoreAntiforgeryToken]
public class DeleteChoreModel : PageModel
{
    private readonly IChoreService _choreService;
    private readonly INotificationService _notificationService;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<DeleteChoreModel> _logger;

    public DeleteChoreModel(
        IChoreService choreService,
        INotificationService notificationService,
        IAuditLogService auditLogService,
        ILogger<DeleteChoreModel> logger)
    {
        _choreService = choreService;
        _notificationService = notificationService;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            // Log request start
            _logger.LogInformation("DeleteChore: Request received from {UserAgent}, Auth={IsAuth}",
                Request.Headers["User-Agent"].ToString(),
                User.Identity?.IsAuthenticated ?? false);

            // Parse JSON body
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            var data = JsonSerializer.Deserialize<DeleteChoreRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            // Validate input
            if (data == null || data.Id <= 0)
            {
                return new JsonResult(new { success = false, message = "Invalid chore ID" })
                {
                    StatusCode = 400
                };
            }

            // Get current user
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var currentUserId))
            {
                _logger.LogWarning("DeleteChore: User not authenticated - IsAuth={IsAuth}, NameId={NameId}",
                    User.Identity?.IsAuthenticated ?? false,
                    userIdClaim ?? "null");
                return new JsonResult(new { success = false, message = "User not authenticated" })
                {
                    StatusCode = 401
                };
            }

            // Check permissions
            if (!await _choreService.CanUserManageChoresAsync(currentUserId))
            {
                _logger.LogWarning("SECURITY: User {UserId} attempted to delete chore {ChoreId} without permission",
                    currentUserId, data.Id);
                return new JsonResult(new { success = false, message = "You do not have permission to delete chores" })
                {
                    StatusCode = 403
                };
            }

            // Get the chore before deleting for notification purposes
            var chore = await _choreService.GetChoreByIdAsync(data.Id);
            if (chore == null)
            {
                return new JsonResult(new { success = false, message = "Chore not found" })
                {
                    StatusCode = 404
                };
            }

            // Delete the chore
            var result = await _choreService.CancelChoreAsync(data.Id);

            if (!result.Success)
            {
                return new JsonResult(new { success = false, message = result.Message })
                {
                    StatusCode = 400
                };
            }

            // Send notification
            await _notificationService.CreateChoreCanceledNotificationAsync(
                chore.UserId,
                chore.Title,
                chore.Date,
                data.Id);

            await _auditLogService.LogAsync(
                action: "ChoreDeletedQuick",
                entityType: "Chore",
                entityId: data.Id,
                description: $"Deleted chore '{chore.Title}' for user {chore.UserId} on {chore.Date:yyyy-MM-dd} via calendar quick-delete",
                details: JsonSerializer.Serialize(new
                {
                    ChoreId = data.Id,
                    ChoreTitle = chore.Title,
                    UserId = chore.UserId,
                    ChoreDate = chore.Date,
                    Source = "CalendarQuickDelete"
                }));

            _logger.LogInformation("Chore deleted via quick-delete: ChoreId={ChoreId}, UserId={UserId}",
                data.Id, currentUserId);

            return new JsonResult(new
            {
                success = true,
                message = "Chore deleted successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting chore via quick-delete");
            return new JsonResult(new { success = false, message = "An error occurred while deleting the chore" })
            {
                StatusCode = 500
            };
        }
    }

    private class DeleteChoreRequest
    {
        public int Id { get; set; }
    }
}
