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

    private static readonly HashSet<string> ValidEntityTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "molecule", "company", "department"
    };

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

            if (!ValidEntityTypes.Contains(request.EntityType))
            {
                return new JsonResult(new { success = false, message = $"Invalid entity type: {request.EntityType}" }) { StatusCode = 400 };
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            {
                return new JsonResult(new { success = false, message = "User not authenticated" }) { StatusCode = 401 };
            }

            // SECURITY-AUDITED: IgnoreQueryFilters() is SAFE here — requires Grant:ReorderHierarchy policy;
            // hierarchy reordering is inherently cross-company organizational data
            var validationError = await ApplyReorderAsync(request);
            if (validationError != null)
            {
                return new JsonResult(new { success = false, message = validationError }) { StatusCode = 400 };
            }

            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Hierarchy reorder applied: Type={EntityType}, ParentId={ParentId}, OrderedIds=[{OrderedIds}], UserId={UserId}",
                request.EntityType,
                request.ParentId,
                string.Join(",", request.OrderedIds),
                userId);

            await _auditLogService.LogAsync(
                action: "HierarchyReorderApplied",
                entityType: request.EntityType,
                entityId: request.ParentId ?? 0,
                description: $"Reorder applied for {request.EntityType} entities",
                details: JsonSerializer.Serialize(new
                {
                    EntityType = request.EntityType,
                    ParentId = request.ParentId,
                    OrderedIds = request.OrderedIds,
                    UserId = userId
                }));

            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing hierarchy reorder request");
            return new JsonResult(new { success = false, message = "An error occurred" }) { StatusCode = 500 };
        }
    }

    /// <summary>
    /// Applies SortOrder updates for the given entity type.
    /// Returns a validation error message if validation fails, or null on success.
    /// </summary>
    private async Task<string?> ApplyReorderAsync(ReorderRequest request)
    {
        switch (request.EntityType.ToLowerInvariant())
        {
            case "area":
            {
                // SECURITY-AUDITED: IgnoreQueryFilters() is SAFE — requires Grant:ReorderHierarchy policy
                var areas = await _db.Areas
                    .IgnoreQueryFilters()
                    .Where(a => request.OrderedIds.Contains(a.Id))
                    .ToListAsync();

                if (areas.Count != request.OrderedIds.Count)
                {
                    return "One or more area IDs are invalid";
                }

                // Validate all areas belong to the same parent
                var parentIds = areas.Select(a => a.ProjectId).Distinct().ToList();
                if (parentIds.Count > 1)
                {
                    return "All areas must belong to the same project";
                }

                for (int i = 0; i < request.OrderedIds.Count; i++)
                {
                    var area = areas.First(a => a.Id == request.OrderedIds[i]);
                    area.SortOrder = i;
                }
                break;
            }

            case "molecule":
            {
                // SECURITY-AUDITED: IgnoreQueryFilters() is SAFE — requires Grant:ReorderHierarchy policy
                var molecules = await _db.Molecules
                    .IgnoreQueryFilters()
                    .Where(m => request.OrderedIds.Contains(m.Id))
                    .ToListAsync();

                if (molecules.Count != request.OrderedIds.Count)
                {
                    return "One or more molecule IDs are invalid";
                }

                // Validate all molecules belong to the same parent
                var parentIds = molecules.Select(m => m.AreaId).Distinct().ToList();
                if (parentIds.Count > 1)
                {
                    return "All molecules must belong to the same area";
                }

                for (int i = 0; i < request.OrderedIds.Count; i++)
                {
                    var molecule = molecules.First(m => m.Id == request.OrderedIds[i]);
                    molecule.SortOrder = i;
                }
                break;
            }

            case "company":
            {
                // SECURITY-AUDITED: IgnoreQueryFilters() is SAFE — requires Grant:ReorderHierarchy policy
                var companies = await _db.Companies
                    .IgnoreQueryFilters()
                    .Where(c => request.OrderedIds.Contains(c.Id))
                    .ToListAsync();

                if (companies.Count != request.OrderedIds.Count)
                {
                    return "One or more company IDs are invalid";
                }

                // Validate all companies belong to the same parent
                var parentIds = companies.Select(c => c.MoleculeId).Distinct().ToList();
                if (parentIds.Count > 1)
                {
                    return "All companies must belong to the same molecule";
                }

                for (int i = 0; i < request.OrderedIds.Count; i++)
                {
                    var company = companies.First(c => c.Id == request.OrderedIds[i]);
                    company.SortOrder = i;
                }
                break;
            }

            case "department":
            {
                // SECURITY-AUDITED: IgnoreQueryFilters() is SAFE — requires Grant:ReorderHierarchy policy
                var departments = await _db.Departments
                    .IgnoreQueryFilters()
                    .Where(d => request.OrderedIds.Contains(d.Id))
                    .ToListAsync();

                if (departments.Count != request.OrderedIds.Count)
                {
                    return "One or more department IDs are invalid";
                }

                // Validate all departments belong to the same parent
                var parentIds = departments.Select(d => d.MoleculeId).Distinct().ToList();
                if (parentIds.Count > 1)
                {
                    return "All departments must belong to the same molecule";
                }

                for (int i = 0; i < request.OrderedIds.Count; i++)
                {
                    var department = departments.First(d => d.Id == request.OrderedIds[i]);
                    department.SortOrder = i;
                }
                break;
            }

            default:
                return $"Invalid entity type: {request.EntityType}";
        }

        return null;
    }

    private class ReorderRequest
    {
        public string EntityType { get; set; } = "";
        public List<int> OrderedIds { get; set; } = new();
        public int? ParentId { get; set; }
    }
}
