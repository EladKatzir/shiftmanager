using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Hubs;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;
using System.Text.Json;

namespace ShiftManager.Pages.Api.Calendar;

[Authorize]
[IgnoreAntiforgeryToken]
public class QuickAddTextEntryModel : PageModel
{
    private readonly ICalendarTextEntryService _textEntryService;
    private readonly IAuditLogService _auditLogService;
    private readonly IGrantService _grantService;
    private readonly ICalendarNotificationService _notificationService;
    private readonly AppDbContext _db;
    private readonly ILogger<QuickAddTextEntryModel> _logger;
    private readonly IStringLocalizer<SharedResources> _localizer;

    public QuickAddTextEntryModel(
        ICalendarTextEntryService textEntryService,
        IAuditLogService auditLogService,
        IGrantService grantService,
        ICalendarNotificationService notificationService,
        AppDbContext db,
        ILogger<QuickAddTextEntryModel> logger,
        IStringLocalizer<SharedResources> localizer)
    {
        _textEntryService = textEntryService;
        _auditLogService = auditLogService;
        _grantService = grantService;
        _notificationService = notificationService;
        _db = db;
        _logger = logger;
        _localizer = localizer;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            var data = JsonSerializer.Deserialize<CreateTextEntryRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (data == null)
            {
                return new JsonResult(new { success = false, message = "Invalid request data" })
                    { StatusCode = 400 };
            }

            if (data.UserId <= 0)
            {
                return new JsonResult(new { success = false, message = "Invalid user" })
                    { StatusCode = 400 };
            }

            if (string.IsNullOrWhiteSpace(data.Text))
            {
                return new JsonResult(new { success = false, message = "Text is required" })
                    { StatusCode = 400 };
            }

            if (data.Text.Length > 200)
            {
                return new JsonResult(new { success = false, message = "Text must not exceed 200 characters" })
                    { StatusCode = 400 };
            }

            if (!DateOnly.TryParse(data.Date, out var entryDate))
            {
                return new JsonResult(new { success = false, message = "Invalid date format" })
                    { StatusCode = 400 };
            }

            if (entryDate > DateOnly.FromDateTime(DateTime.Today.AddYears(2)))
            {
                return new JsonResult(new { success = false, message = "Cannot create entries more than 2 years in the future" })
                    { StatusCode = 400 };
            }

            if (entryDate < DateOnly.FromDateTime(DateTime.Today))
            {
                return new JsonResult(new { success = false, message = "Cannot create entries in the past" })
                    { StatusCode = 400 };
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var currentUserId))
            {
                return new JsonResult(new { success = false, message = "User not authenticated" })
                    { StatusCode = 401 };
            }

            // SECURITY: caller must have note-writing permission (WriteOverviewNotes OR any calendar-edit grant)
            if (!await _grantService.HasCalendarNotePermissionAsync(currentUserId))
            {
                _logger.LogWarning("SECURITY: User {UserId} attempted to create text entry without note permission", currentUserId);
                return new JsonResult(new { success = false, message = "You do not have permission to create text entries" })
                    { StatusCode = 403 };
            }

            // SECURITY: verify the target user is within the caller's accessible scope
            // (managers with assign grants: cross-company within molecule; note-only tier: own company only)
            if (!await _grantService.CanReachUserForNoteAsync(currentUserId, data.UserId))
            {
                _logger.LogWarning("SECURITY: User {UserId} attempted to write note on out-of-scope user {TargetUserId}",
                    currentUserId, data.UserId);
                return new JsonResult(new { success = false, message = "Target user is outside your accessible scope" })
                    { StatusCode = 403 };
            }

            var entry = await _textEntryService.AddAsync(data.UserId, entryDate, data.Text.Trim(), currentUserId);

            await _auditLogService.LogAsync(
                action: "TextEntryCreated",
                entityType: "CalendarTextEntry",
                entityId: entry.Id,
                description: $"Created text entry '{data.Text}' for user {data.UserId} on {entryDate:yyyy-MM-dd} via Quick Entry");

            try
            {
                var moleculeId = await _db.Companies
                    .IgnoreQueryFilters()
                    .Where(c => c.Id == entry.CompanyId)
                    .Select(c => c.MoleculeId)
                    .FirstOrDefaultAsync();

                if (moleculeId.HasValue)
                {
                    await _notificationService.NotifyTextEntryChangedAsync(
                        CalendarGroups.Shifts(moleculeId.Value, null),
                        new CalendarTextEntryChangedEvent(entry.Id, entry.UserId, entry.Date, entry.Text, "created"));
                }
                else
                {
                    _logger.LogWarning("TextEntry {EntryId} has CompanyId={CompanyId} with no Molecule — SignalR notification skipped",
                        entry.Id, entry.CompanyId);
                }
            }
            catch (Exception signalREx)
            {
                _logger.LogWarning(signalREx, "SignalR notification failed for text entry {EntryId}", entry.Id);
            }

            return new JsonResult(new
            {
                success = true,
                textEntryId = entry.Id,
                message = "Text entry created successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating text entry via Quick Entry");
            return new JsonResult(
                ShiftManager.Models.ApiErrorResponse.Create(
                    "ERROR_CALENDAR_CREATE_TEXT_ENTRY_FAILED",
                    _localizer["Error_CalendarApi_CreateTextEntryFailed"].Value)
                .WithCorrelationId(HttpContext.TraceIdentifier))
                { StatusCode = 500 };
        }
    }

    private class CreateTextEntryRequest
    {
        public int UserId { get; set; }
        public string Date { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }
}
