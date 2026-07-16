using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;
using System.Text.Json;

namespace ShiftManager.Pages.Api.Calendar;

/// <summary>
/// Manual time-off entry from a people-row calendar cell (Vacation / DayAt / After).
/// Thin adapter over <see cref="IVacationApprovalService.CreateApprovedManualTimeOffAsync"/>:
/// parses the JSON body, resolves the actor + viewed company, maps the type string, and
/// localizes the service's ErrorKey. CSRF is enforced by ApiAuthenticationMiddleware's
/// X-Requested-With requirement (hence [IgnoreAntiforgeryToken]); the /Api/Calendar prefix
/// is already whitelisted, so no Program.cs / middleware registration is needed.
/// </summary>
[Authorize]
[IgnoreAntiforgeryToken]
public class QuickAddTimeOffModel : PageModel
{
    private readonly IVacationApprovalService _vacationService;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly ILogger<QuickAddTimeOffModel> _logger;

    public QuickAddTimeOffModel(
        IVacationApprovalService vacationService,
        IStringLocalizer<SharedResources> localizer,
        ILogger<QuickAddTimeOffModel> logger)
    {
        _vacationService = vacationService;
        _localizer = localizer;
        _logger = logger;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            var data = JsonSerializer.Deserialize<TimeOffEntryRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (data == null)
                return new JsonResult(new { success = false, message = "Invalid request data" }) { StatusCode = 400 };

            if (data.UserId <= 0)
                return new JsonResult(new { success = false, message = "Invalid user" }) { StatusCode = 400 };

            if (!DateOnly.TryParse(data.Date, out var entryDate))
                return new JsonResult(new { success = false, message = "Invalid date format" }) { StatusCode = 400 };

            if (entryDate < DateOnly.FromDateTime(DateTime.Today))
                return new JsonResult(new { success = false, message = "Cannot enter time off in the past" }) { StatusCode = 400 };

            if (entryDate > DateOnly.FromDateTime(DateTime.Today.AddYears(2)))
                return new JsonResult(new { success = false, message = "Cannot enter time off more than 2 years ahead" }) { StatusCode = 400 };

            if (!TryMapType(data.Type, out var timeOffType))
                return new JsonResult(new { success = false, message = _localizer["Error_TimeOff_InvalidType"].Value }) { StatusCode = 400 };

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var currentUserId))
                return new JsonResult(new { success = false, message = "User not authenticated" }) { StatusCode = 401 };

            var (success, requestId, errorKey) = await _vacationService.CreateApprovedManualTimeOffAsync(
                data.UserId, timeOffType, entryDate, entryDate, data.Label, currentUserId);

            if (!success)
            {
                var status = errorKey switch
                {
                    "Error_TimeOff_NoPermission" => 403,
                    "Error_TimeOff_UserNotInCompany" => 403,
                    "Error_TimeOff_OverlapExists" => 409,
                    _ => 400
                };
                var msg = errorKey != null
                    ? _localizer[errorKey].Value
                    : _localizer["Error_CalendarApi_TimeOffFailed"].Value;
                _logger.LogInformation("Manual time-off entry rejected for user {UserId} by {Actor}: {ErrorKey}",
                    data.UserId, currentUserId, errorKey);
                return new JsonResult(new { success = false, message = msg }) { StatusCode = status };
            }

            return new JsonResult(new { success = true, requestId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error entering time off via Quick Entry");
            return new JsonResult(
                ApiErrorResponse.Create(
                    "ERROR_CALENDAR_TIMEOFF_FAILED",
                    _localizer["Error_CalendarApi_TimeOffFailed"].Value)
                .WithCorrelationId(HttpContext.TraceIdentifier))
                { StatusCode = 500 };
        }
    }

    private static bool TryMapType(string? type, out TimeOffType result)
    {
        switch ((type ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "vacation": result = TimeOffType.Vacation; return true;
            case "after": result = TimeOffType.After; return true;
            case "dayat": result = TimeOffType.DayAt; return true;
            default: result = TimeOffType.Vacation; return false;
        }
    }

    private class TimeOffEntryRequest
    {
        public int UserId { get; set; }
        public string Date { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string? Label { get; set; }
    }
}
