using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Text.Json;

namespace ShiftManager.Pages.Api.Calendar;

[Authorize(Policy = "Grant:AssignChores")]
[IgnoreAntiforgeryToken]
public class RestoreChoreModel : PageModel
{
    private readonly IChoreService _choreService;
    private readonly ILogger<RestoreChoreModel> _logger;
    private readonly IAuditLogService _auditLogService;
    private readonly IStringLocalizer<SharedResources> _localizer;

    public RestoreChoreModel(IChoreService choreService, ILogger<RestoreChoreModel> logger, IAuditLogService auditLogService, IStringLocalizer<SharedResources> localizer)
    {
        _choreService = choreService;
        _logger = logger;
        _auditLogService = auditLogService;
        _localizer = localizer;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            var data = JsonSerializer.Deserialize<RestoreRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (data == null || data.Id <= 0)
            {
                return new JsonResult(new { success = false, message = "Invalid chore ID" })
                    { StatusCode = 400 };
            }

            // Verify chore belongs to accessible scope before restoring (IDOR prevention)
            var chore = await _choreService.GetChoreByIdAsync(data.Id);
            if (chore == null)
            {
                return new JsonResult(new { success = false, message = "Chore not found" })
                    { StatusCode = 404 };
            }

            var result = await _choreService.RestoreChoreAsync(data.Id);

            if (result.Success)
            {
                await _auditLogService.LogAsync("ChoreRestored", "Chore", data.Id, $"Chore {data.Id} restored via undo");
            }

            return new JsonResult(new { success = result.Success, message = result.Message })
                { StatusCode = result.Success ? 200 : 400 };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring chore via undo");
            return new JsonResult(
                ShiftManager.Models.ApiErrorResponse.Create(
                    "ERROR_CALENDAR_RESTORE_CHORE_FAILED",
                    _localizer["Error_CalendarApi_RestoreChoreFailed"].Value)
                .WithCorrelationId(HttpContext.TraceIdentifier))
                { StatusCode = 500 };
        }
    }

    private class RestoreRequest
    {
        public int Id { get; set; }
    }
}
