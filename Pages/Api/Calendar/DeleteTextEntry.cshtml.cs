using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Services;
using System.Security.Claims;
using System.Text.Json;

namespace ShiftManager.Pages.Api.Calendar;

[Authorize]
[IgnoreAntiforgeryToken]
public class DeleteTextEntryModel : PageModel
{
    private readonly ICalendarTextEntryService _textEntryService;
    private readonly IAuditLogService _auditLogService;
    private readonly IGrantService _grantService;
    private readonly ILogger<DeleteTextEntryModel> _logger;

    public DeleteTextEntryModel(
        ICalendarTextEntryService textEntryService,
        IAuditLogService auditLogService,
        IGrantService grantService,
        ILogger<DeleteTextEntryModel> logger)
    {
        _textEntryService = textEntryService;
        _auditLogService = auditLogService;
        _grantService = grantService;
        _logger = logger;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            var data = JsonSerializer.Deserialize<DeleteTextEntryRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (data == null || data.Id <= 0)
            {
                return new JsonResult(new { success = false, message = "Invalid request" })
                    { StatusCode = 400 };
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var currentUserId))
            {
                return new JsonResult(new { success = false, message = "User not authenticated" })
                    { StatusCode = 401 };
            }

            // SECURITY: Verify caller has calendar editing permissions (shift, chore, or on-duty)
            if (!await _grantService.HasCalendarEditPermissionAsync(currentUserId))
            {
                _logger.LogWarning("SECURITY: User {UserId} attempted to delete text entry {EntryId} without calendar edit permissions",
                    currentUserId, data.Id);
                return new JsonResult(new { success = false, message = "You do not have permission to delete calendar entries" })
                    { StatusCode = 403 };
            }

            // Load entry before deletion for audit context
            var entry = await _textEntryService.GetByIdAsync(data.Id);
            if (entry == null)
            {
                return new JsonResult(new { success = false, message = "Text entry not found" })
                    { StatusCode = 404 };
            }

            var deleted = await _textEntryService.DeleteAsync(data.Id);
            if (!deleted)
            {
                return new JsonResult(new { success = false, message = "Text entry not found" })
                    { StatusCode = 404 };
            }

            await _auditLogService.LogAsync(
                action: "TextEntryDeleted",
                entityType: "CalendarTextEntry",
                entityId: data.Id,
                description: $"Deleted text entry '{entry.Text}' for user {entry.UserId} on {entry.Date:yyyy-MM-dd}");

            return new JsonResult(new { success = true, message = "Text entry deleted" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting text entry");
            return new JsonResult(new { success = false, message = "An error occurred" })
                { StatusCode = 500 };
        }
    }

    private class DeleteTextEntryRequest
    {
        public int Id { get; set; }
    }
}
