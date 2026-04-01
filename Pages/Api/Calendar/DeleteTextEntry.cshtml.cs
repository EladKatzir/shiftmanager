using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using ShiftManager.Resources;
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
    private readonly IStringLocalizer<SharedResources> _localizer;

    public DeleteTextEntryModel(
        ICalendarTextEntryService textEntryService,
        IAuditLogService auditLogService,
        IGrantService grantService,
        ILogger<DeleteTextEntryModel> logger,
        IStringLocalizer<SharedResources> localizer)
    {
        _textEntryService = textEntryService;
        _auditLogService = auditLogService;
        _grantService = grantService;
        _logger = logger;
        _localizer = localizer;
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
                return new JsonResult(new { success = false, message = _localizer["DeleteTextEntry_InvalidRequest"].Value })
                    { StatusCode = 400 };
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var currentUserId))
            {
                return new JsonResult(new { success = false, message = _localizer["DeleteTextEntry_UserNotAuthenticated"].Value })
                    { StatusCode = 401 };
            }

            // SECURITY: Verify caller has calendar editing permissions (shift, chore, or on-duty)
            if (!await _grantService.HasCalendarEditPermissionAsync(currentUserId))
            {
                _logger.LogWarning("SECURITY: User {UserId} attempted to delete text entry {EntryId} without calendar edit permissions",
                    currentUserId, data.Id);
                return new JsonResult(new { success = false, message = _localizer["DeleteTextEntry_NoPermissionDelete"].Value })
                    { StatusCode = 403 };
            }

            // Load entry before deletion for audit context
            var entry = await _textEntryService.GetByIdAsync(data.Id);
            if (entry == null)
            {
                return new JsonResult(new { success = false, message = _localizer["DeleteTextEntry_NotFound"].Value })
                    { StatusCode = 404 };
            }

            // SECURITY: OverviewNote entries must be deleted via the Overview page handler
            // which enforces the WriteOverviewNotes grant — not via this endpoint
            if (entry.EntryType == Models.CalendarTextEntryType.OverviewNote)
            {
                _logger.LogWarning("SECURITY: User {UserId} attempted to delete OverviewNote {EntryId} via DeleteTextEntry endpoint",
                    currentUserId, data.Id);
                return new JsonResult(new { success = false, message = _localizer["DeleteTextEntry_CannotDeleteOverviewNotes"].Value })
                    { StatusCode = 403 };
            }

            var deleted = await _textEntryService.DeleteAsync(data.Id);
            if (!deleted)
            {
                return new JsonResult(new { success = false, message = _localizer["DeleteTextEntry_NotFound"].Value })
                    { StatusCode = 404 };
            }

            await _auditLogService.LogAsync(
                action: "TextEntryDeleted",
                entityType: "CalendarTextEntry",
                entityId: data.Id,
                description: $"Deleted text entry '{entry.Text}' for user {entry.UserId} on {entry.Date:yyyy-MM-dd}");

            return new JsonResult(new { success = true, message = _localizer["DeleteTextEntry_Success"].Value });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting text entry");
            return new JsonResult(new { success = false, message = _localizer["DeleteTextEntry_Error"].Value })
                { StatusCode = 500 };
        }
    }

    private class DeleteTextEntryRequest
    {
        public int Id { get; set; }
    }
}
