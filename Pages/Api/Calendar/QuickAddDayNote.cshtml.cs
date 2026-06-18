using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;
using System.Text.Json;

namespace ShiftManager.Pages.Api.Calendar;

// Day-scoped free-text note from Quick Entry, available in any calendar view (shift-mode or user-mode).
// Sibling of QuickAddTextEntry, but the note attaches to a DAY (not a user): a shift-type cell has no
// single user, so day notes are keyed by (Date, CompanyId). Covered by ApiAuthenticationMiddleware's
// "/Api/Calendar" internal-web-UI prefix — no separate middleware registration needed.
[Authorize]
[IgnoreAntiforgeryToken]
public class QuickAddDayNoteModel : PageModel
{
    private readonly ICalendarDayNoteService _dayNoteService;
    private readonly IAuditLogService _auditLogService;
    private readonly IGrantService _grantService;
    private readonly ITenantResolver _tenantResolver;
    private readonly ILogger<QuickAddDayNoteModel> _logger;
    private readonly IStringLocalizer<SharedResources> _localizer;

    public QuickAddDayNoteModel(
        ICalendarDayNoteService dayNoteService,
        IAuditLogService auditLogService,
        IGrantService grantService,
        ITenantResolver tenantResolver,
        ILogger<QuickAddDayNoteModel> logger,
        IStringLocalizer<SharedResources> localizer)
    {
        _dayNoteService = dayNoteService;
        _auditLogService = auditLogService;
        _grantService = grantService;
        _tenantResolver = tenantResolver;
        _logger = logger;
        _localizer = localizer;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            var data = JsonSerializer.Deserialize<DayNoteRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (data == null)
            {
                return new JsonResult(new { success = false, message = "Invalid request data" })
                    { StatusCode = 400 };
            }

            if (!DateOnly.TryParse(data.Date, out var noteDate))
            {
                return new JsonResult(new { success = false, message = "Invalid date format" })
                    { StatusCode = 400 };
            }

            // Notes are informational — unlike assignments, past dates are allowed (annotate yesterday).
            // Only bound the far future to keep input sane.
            if (noteDate > DateOnly.FromDateTime(DateTime.Today.AddYears(2)))
            {
                return new JsonResult(new { success = false, message = "Cannot create notes more than 2 years in the future" })
                    { StatusCode = 400 };
            }

            var text = (data.Text ?? string.Empty).Trim();
            if (text.Length > 500)
            {
                return new JsonResult(new { success = false, message = "Text must not exceed 500 characters" })
                    { StatusCode = 400 };
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var currentUserId))
            {
                return new JsonResult(new { success = false, message = "User not authenticated" })
                    { StatusCode = 401 };
            }

            // SECURITY: same note-write gate as user-scoped text entries.
            if (!await _grantService.HasCalendarNotePermissionAsync(currentUserId))
            {
                _logger.LogWarning("SECURITY: User {UserId} attempted to write a day note without note permission", currentUserId);
                return new JsonResult(new { success = false, message = "You do not have permission to create notes" })
                    { StatusCode = 403 };
            }

            var companyId = _tenantResolver.GetCurrentTenantId();
            if (companyId <= 0)
            {
                return new JsonResult(new { success = false, message = "No active company context" })
                    { StatusCode = 400 };
            }

            // Empty text clears the day note; non-empty upserts it.
            if (text.Length == 0)
            {
                var removed = await _dayNoteService.DeleteDayNoteAsync(noteDate, companyId);
                if (removed)
                {
                    await _auditLogService.LogAsync(
                        action: "DayNoteDeleted",
                        entityType: "CalendarDayNote",
                        entityId: 0,
                        description: $"Cleared day note on {noteDate:yyyy-MM-dd} via Quick Entry");
                }
                return new JsonResult(new { success = true, deleted = true, message = "Day note cleared" });
            }

            var note = await _dayNoteService.SetDayNoteAsync(noteDate, companyId, text, currentUserId);

            await _auditLogService.LogAsync(
                action: "DayNoteSaved",
                entityType: "CalendarDayNote",
                entityId: note.Id,
                description: $"Saved day note '{text}' on {noteDate:yyyy-MM-dd} via Quick Entry");

            return new JsonResult(new
            {
                success = true,
                dayNoteId = note.Id,
                message = "Day note saved successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving day note via Quick Entry");
            return new JsonResult(
                ShiftManager.Models.ApiErrorResponse.Create(
                    "ERROR_CALENDAR_SAVE_DAY_NOTE_FAILED",
                    _localizer["Error_CalendarApi_CreateTextEntryFailed"].Value)
                .WithCorrelationId(HttpContext.TraceIdentifier))
                { StatusCode = 500 };
        }
    }

    private class DayNoteRequest
    {
        public string Date { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }
}
