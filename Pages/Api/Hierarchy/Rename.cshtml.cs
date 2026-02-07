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
/// API endpoint to rename hierarchy entities (inline edit).
/// POST /Api/Hierarchy/Rename
/// </summary>
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:EditHierarchy policy;
// hierarchy rename needs cross-company entity lookups
[Authorize(Policy = "Grant:EditHierarchy")]
[IgnoreAntiforgeryToken]
public class RenameModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<RenameModel> _logger;

    public RenameModel(
        AppDbContext db,
        IAuditLogService auditLogService,
        ILogger<RenameModel> logger)
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
            var request = JsonSerializer.Deserialize<RenameRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (request == null || string.IsNullOrEmpty(request.EntityType) || request.EntityId <= 0 || string.IsNullOrWhiteSpace(request.Name))
            {
                return new JsonResult(new { success = false, message = "Invalid request data" }) { StatusCode = 400 };
            }

            // Validate name length
            if (request.Name.Length > 100)
            {
                return new JsonResult(new { success = false, message = "Name must not exceed 100 characters" }) { StatusCode = 400 };
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            {
                return new JsonResult(new { success = false, message = "User not authenticated" }) { StatusCode = 401 };
            }

            string oldName;
            string result;

            switch (request.EntityType.ToLower())
            {
                case "project":
                    var project = await _db.Projects.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == request.EntityId);
                    if (project == null)
                    {
                        return new JsonResult(new { success = false, message = "Project not found" }) { StatusCode = 404 };
                    }
                    oldName = project.DisplayName;
                    project.DisplayName = request.Name.Trim();
                    result = $"Project renamed from '{oldName}' to '{request.Name}'";
                    break;

                case "area":
                    var area = await _db.Areas.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == request.EntityId);
                    if (area == null)
                    {
                        return new JsonResult(new { success = false, message = "Area not found" }) { StatusCode = 404 };
                    }
                    oldName = area.DisplayName;
                    area.DisplayName = request.Name.Trim();
                    result = $"Area renamed from '{oldName}' to '{request.Name}'";
                    break;

                case "molecule":
                    var molecule = await _db.Molecules.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == request.EntityId);
                    if (molecule == null)
                    {
                        return new JsonResult(new { success = false, message = "Molecule not found" }) { StatusCode = 404 };
                    }
                    oldName = molecule.DisplayName;
                    molecule.DisplayName = request.Name.Trim();
                    result = $"Molecule renamed from '{oldName}' to '{request.Name}'";
                    break;

                case "company":
                    var company = await _db.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == request.EntityId);
                    if (company == null)
                    {
                        return new JsonResult(new { success = false, message = "Company not found" }) { StatusCode = 404 };
                    }
                    oldName = company.DisplayName ?? company.Name;
                    company.DisplayName = request.Name.Trim();
                    result = $"Company renamed from '{oldName}' to '{request.Name}'";
                    break;

                case "department":
                    var department = await _db.Departments.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Id == request.EntityId);
                    if (department == null)
                    {
                        return new JsonResult(new { success = false, message = "Department not found" }) { StatusCode = 404 };
                    }
                    oldName = department.DisplayName;
                    department.DisplayName = request.Name.Trim();
                    result = $"Department renamed from '{oldName}' to '{request.Name}'";
                    break;

                default:
                    return new JsonResult(new { success = false, message = $"Invalid entity type: {request.EntityType}" }) { StatusCode = 400 };
            }

            await _db.SaveChangesAsync();

            _logger.LogInformation("Hierarchy rename: {Result}, UserId={UserId}", result, userId);

            await _auditLogService.LogAsync(
                action: "HierarchyEntityRenamed",
                entityType: request.EntityType,
                entityId: request.EntityId,
                description: result,
                details: JsonSerializer.Serialize(new
                {
                    EntityType = request.EntityType,
                    EntityId = request.EntityId,
                    OldName = oldName,
                    NewName = request.Name,
                    UserId = userId
                }));

            return new JsonResult(new { success = true, message = result });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing hierarchy rename request");
            return new JsonResult(new { success = false, message = "An error occurred" }) { StatusCode = 500 };
        }
    }

    private class RenameRequest
    {
        public string EntityType { get; set; } = "";
        public int EntityId { get; set; }
        public string Name { get; set; } = "";
    }
}
