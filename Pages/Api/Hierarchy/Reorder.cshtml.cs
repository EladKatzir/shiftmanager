using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Services;
using System.Security.Claims;
using System.Text.Json;

namespace ShiftManager.Pages.Api.Hierarchy;

/// <summary>
/// API endpoint to reorder hierarchy entities (drag-drop reordering).
/// POST /Api/Hierarchy/Reorder
/// </summary>
[Authorize(Policy = "Grant:ReorderHierarchy")]
[IgnoreAntiforgeryToken]
public class ReorderModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<ReorderModel> _logger;

    public ReorderModel(
        AppDbContext db,
        IAuditLogService auditLogService,
        ILogger<ReorderModel> logger)
    {
        _db = db;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            var request = JsonSerializer.Deserialize<ReorderRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (request == null || string.IsNullOrEmpty(request.EntityType) || request.OrderedIds == null || !request.OrderedIds.Any())
            {
                return new JsonResult(new { success = false, message = "Invalid request data" }) { StatusCode = 400 };
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            {
                return new JsonResult(new { success = false, message = "User not authenticated" }) { StatusCode = 401 };
            }

            // Note: Currently, ordering is based on Name. For explicit ordering, we would need
            // to add a SortOrder column to each entity. For now, this API is a placeholder
            // that logs the intended order for future implementation.
            _logger.LogInformation(
                "Hierarchy reorder requested: Type={EntityType}, ParentId={ParentId}, OrderedIds=[{OrderedIds}], UserId={UserId}",
                request.EntityType,
                request.ParentId,
                string.Join(",", request.OrderedIds),
                userId);

            await _auditLogService.LogAsync(
                action: "HierarchyReorderRequested",
                entityType: request.EntityType,
                entityId: request.ParentId ?? 0,
                description: $"Reorder requested for {request.EntityType} entities",
                details: JsonSerializer.Serialize(new
                {
                    EntityType = request.EntityType,
                    ParentId = request.ParentId,
                    OrderedIds = request.OrderedIds,
                    UserId = userId
                }));

            // TODO: Implement actual reordering when SortOrder columns are added
            // For now, return success to allow the UI to work
            return new JsonResult(new { success = true, message = "Reorder logged (explicit ordering not yet implemented)" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing hierarchy reorder request");
            return new JsonResult(new { success = false, message = "An error occurred" }) { StatusCode = 500 };
        }
    }

    private class ReorderRequest
    {
        public string EntityType { get; set; } = "";
        public List<int> OrderedIds { get; set; } = new();
        public int? ParentId { get; set; }
    }
}
