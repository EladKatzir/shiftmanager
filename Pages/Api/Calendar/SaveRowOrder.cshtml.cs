using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Services;
using System.Security.Claims;
using System.Text.Json;

namespace ShiftManager.Pages.Api.Calendar;

[Authorize]
[IgnoreAntiforgeryToken]
public class SaveRowOrderModel : PageModel
{
    private readonly ICalendarRowOrderService _rowOrder;
    private readonly IAuditLogService _audit;
    private readonly ILogger<SaveRowOrderModel> _logger;

    public SaveRowOrderModel(
        ICalendarRowOrderService rowOrder,
        IAuditLogService audit,
        ILogger<SaveRowOrderModel> logger)
    {
        _rowOrder = rowOrder;
        _audit = audit;
        _logger = logger;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        SaveRowOrderRequest? data;
        try
        {
            data = JsonSerializer.Deserialize<SaveRowOrderRequest>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "SaveRowOrder: malformed JSON body");
            return new JsonResult(new { success = false, message = "Invalid JSON" }) { StatusCode = 400 };
        }

        if (data == null || string.IsNullOrWhiteSpace(data.ContextKey) || data.ItemIds == null)
            return new JsonResult(new { success = false, message = "Invalid request" }) { StatusCode = 400 };

        if (data.ContextKey.Length > 128 || (data.GroupId?.Length ?? 0) > 64)
            return new JsonResult(new { success = false, message = "Key too long" }) { StatusCode = 400 };

        if (data.ItemIds.Count == 0 || data.ItemIds.Count > 500
            || data.ItemIds.Any(s => string.IsNullOrEmpty(s) || s.Length > 64))
            return new JsonResult(new { success = false, message = "Invalid items" }) { StatusCode = 400 };

        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(idClaim, out var userId))
            return new JsonResult(new { success = false, message = "Unauthenticated" }) { StatusCode = 401 };

        var groupId = data.GroupId ?? "";
        await _rowOrder.SaveOrderAsync(userId, data.ContextKey, groupId, data.ItemIds);

        await _audit.LogAsync(
            action: "CalendarOrderChanged",
            entityType: "UserCalendarRowOrder",
            entityId: userId,
            description: $"Reordered {(string.IsNullOrEmpty(groupId) ? "categories" : "rows in " + groupId)} for {data.ContextKey}");

        _logger.LogInformation("SaveRowOrder: user {UserId} saved order for {ContextKey}/{GroupId} ({Count} items)",
            userId, data.ContextKey, groupId, data.ItemIds.Count);

        return new JsonResult(new { success = true });
    }

    private sealed class SaveRowOrderRequest
    {
        public string ContextKey { get; set; } = string.Empty;
        public string? GroupId { get; set; }
        public List<string> ItemIds { get; set; } = new();
    }
}
