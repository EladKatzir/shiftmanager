using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Services;
using System.Security.Claims;
using System.Text.Json;

namespace ShiftManager.Pages.Api.Calendar;

/// <summary>
/// Persists a user's personal calendar column widths. Authenticated but grant-less — a column
/// width is a personal UI preference with no cross-tenant read path, exactly like row order
/// (<see cref="SaveRowOrderModel"/>).
/// </summary>
[Authorize]
[IgnoreAntiforgeryToken]
public class SaveColumnWidthsModel : PageModel
{
    /// <summary>Narrowest a column may be dragged. Sourced from the column planner so the drag
    /// handle, the DOM and this validator can never disagree.</summary>
    public const int MinWidth = ShiftManager.ViewComponents.CalendarColumnPlanner.MinWidth;

    /// <summary>Widest a column may be dragged. See <see cref="MinWidth"/>.</summary>
    public const int MaxWidth = ShiftManager.ViewComponents.CalendarColumnPlanner.MaxWidth;

    /// <summary>A period can show at most ~31 days plus the label and total columns; 400 is a
    /// generous ceiling that still bounds the write.</summary>
    public const int MaxColumns = 400;

    private readonly ICalendarColumnWidthService _widths;
    private readonly IAuditLogService _audit;
    private readonly ILogger<SaveColumnWidthsModel> _logger;

    public SaveColumnWidthsModel(
        ICalendarColumnWidthService widths,
        IAuditLogService audit,
        ILogger<SaveColumnWidthsModel> logger)
    {
        _widths = widths;
        _audit = audit;
        _logger = logger;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        SaveColumnWidthsRequest? data;
        try
        {
            data = JsonSerializer.Deserialize<SaveColumnWidthsRequest>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "SaveColumnWidths: malformed JSON body");
            return new JsonResult(new { success = false, message = "Invalid JSON" }) { StatusCode = 400 };
        }

        if (data == null || string.IsNullOrWhiteSpace(data.ContextKey) || data.Widths == null)
            return new JsonResult(new { success = false, message = "Invalid request" }) { StatusCode = 400 };

        if (data.ContextKey.Length > 128)
            return new JsonResult(new { success = false, message = "Key too long" }) { StatusCode = 400 };

        if (data.Widths.Count > MaxColumns)
            return new JsonResult(new { success = false, message = "Too many columns" }) { StatusCode = 400 };

        // Reject rather than clamp: the client clamps during the drag, so an out-of-range value means
        // the caller is not our UI. Coercing it silently would hide that.
        foreach (var (columnKey, width) in data.Widths)
        {
            if (string.IsNullOrEmpty(columnKey) || columnKey.Length > 64)
                return new JsonResult(new { success = false, message = "Invalid column key" }) { StatusCode = 400 };

            if (width < MinWidth || width > MaxWidth)
                return new JsonResult(new { success = false, message = "Width out of range" }) { StatusCode = 400 };
        }

        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(idClaim, out var userId))
            return new JsonResult(new { success = false, message = "Unauthenticated" }) { StatusCode = 401 };

        await _widths.SaveWidthsAsync(userId, data.ContextKey, data.Widths);

        await _audit.LogAsync(
            action: "CalendarColumnWidthsChanged",
            entityType: "UserCalendarColumnWidth",
            entityId: userId,
            description: $"Set {data.Widths.Count} column width(s) for {data.ContextKey}");

        _logger.LogInformation("SaveColumnWidths: user {UserId} saved {Count} width(s) for {ContextKey}",
            userId, data.Widths.Count, data.ContextKey);

        return new JsonResult(new { success = true });
    }

    private sealed class SaveColumnWidthsRequest
    {
        public string ContextKey { get; set; } = string.Empty;
        public Dictionary<string, int> Widths { get; set; } = new();
    }
}
