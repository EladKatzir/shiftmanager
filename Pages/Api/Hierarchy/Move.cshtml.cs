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
/// API endpoint to move hierarchy entities to a new parent.
/// POST /Api/Hierarchy/Move
/// </summary>
[Authorize(Policy = "Grant:ReorderHierarchy")]
[IgnoreAntiforgeryToken]
public class MoveModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<MoveModel> _logger;

    public MoveModel(
        AppDbContext db,
        IAuditLogService auditLogService,
        ILogger<MoveModel> logger)
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
            var request = JsonSerializer.Deserialize<MoveRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (request == null || string.IsNullOrEmpty(request.EntityType) || request.EntityId <= 0)
            {
                return new JsonResult(new { success = false, message = "Invalid request data" }) { StatusCode = 400 };
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            {
                return new JsonResult(new { success = false, message = "User not authenticated" }) { StatusCode = 401 };
            }

            string result;
            int oldParentId = 0;

            switch (request.EntityType.ToLower())
            {
                case "area":
                    var area = await _db.Areas.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == request.EntityId);
                    if (area == null)
                    {
                        return new JsonResult(new { success = false, message = "Area not found" }) { StatusCode = 404 };
                    }
                    if (!request.NewParentId.HasValue)
                    {
                        return new JsonResult(new { success = false, message = "New parent project ID required" }) { StatusCode = 400 };
                    }
                    var targetProject = await _db.Projects.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == request.NewParentId);
                    if (targetProject == null)
                    {
                        return new JsonResult(new { success = false, message = "Target project not found" }) { StatusCode = 404 };
                    }
                    oldParentId = area.ProjectId;
                    area.ProjectId = request.NewParentId.Value;
                    result = $"Area '{area.Name}' moved from project {oldParentId} to project {request.NewParentId}";
                    break;

                case "molecule":
                    var molecule = await _db.Molecules.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == request.EntityId);
                    if (molecule == null)
                    {
                        return new JsonResult(new { success = false, message = "Molecule not found" }) { StatusCode = 404 };
                    }
                    if (!request.NewParentId.HasValue)
                    {
                        return new JsonResult(new { success = false, message = "New parent area ID required" }) { StatusCode = 400 };
                    }
                    var targetArea = await _db.Areas.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == request.NewParentId);
                    if (targetArea == null)
                    {
                        return new JsonResult(new { success = false, message = "Target area not found" }) { StatusCode = 404 };
                    }
                    oldParentId = molecule.AreaId;
                    molecule.AreaId = request.NewParentId.Value;
                    result = $"Molecule '{molecule.Name}' moved from area {oldParentId} to area {request.NewParentId}";
                    break;

                case "company":
                    var company = await _db.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == request.EntityId);
                    if (company == null)
                    {
                        return new JsonResult(new { success = false, message = "Company not found" }) { StatusCode = 404 };
                    }
                    if (!request.NewParentId.HasValue)
                    {
                        return new JsonResult(new { success = false, message = "New parent molecule ID required" }) { StatusCode = 400 };
                    }
                    var targetMolecule = await _db.Molecules.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == request.NewParentId);
                    if (targetMolecule == null)
                    {
                        return new JsonResult(new { success = false, message = "Target molecule not found" }) { StatusCode = 404 };
                    }
                    oldParentId = company.MoleculeId ?? 0;
                    company.MoleculeId = request.NewParentId.Value;
                    result = $"Company '{company.Name}' moved from molecule {oldParentId} to molecule {request.NewParentId}";
                    break;

                case "department":
                    var department = await _db.Departments.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Id == request.EntityId);
                    if (department == null)
                    {
                        return new JsonResult(new { success = false, message = "Department not found" }) { StatusCode = 404 };
                    }
                    if (!request.NewParentId.HasValue)
                    {
                        return new JsonResult(new { success = false, message = "New parent molecule ID required" }) { StatusCode = 400 };
                    }
                    var targetMoleculeForDept = await _db.Molecules.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == request.NewParentId);
                    if (targetMoleculeForDept == null)
                    {
                        return new JsonResult(new { success = false, message = "Target molecule not found" }) { StatusCode = 404 };
                    }
                    oldParentId = department.MoleculeId;
                    department.MoleculeId = request.NewParentId.Value;
                    result = $"Department '{department.Name}' moved from molecule {oldParentId} to molecule {request.NewParentId}";
                    break;

                default:
                    return new JsonResult(new { success = false, message = $"Invalid entity type: {request.EntityType}" }) { StatusCode = 400 };
            }

            await _db.SaveChangesAsync();

            _logger.LogInformation("Hierarchy move: {Result}, UserId={UserId}", result, userId);

            await _auditLogService.LogAsync(
                action: "HierarchyEntityMoved",
                entityType: request.EntityType,
                entityId: request.EntityId,
                description: result,
                details: JsonSerializer.Serialize(new
                {
                    EntityType = request.EntityType,
                    EntityId = request.EntityId,
                    OldParentId = oldParentId,
                    NewParentId = request.NewParentId,
                    UserId = userId
                }));

            return new JsonResult(new { success = true, message = result });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing hierarchy move request");
            return new JsonResult(new { success = false, message = "An error occurred" }) { StatusCode = 500 };
        }
    }

    private class MoveRequest
    {
        public string EntityType { get; set; } = "";
        public int EntityId { get; set; }
        public int? NewParentId { get; set; }
    }
}
