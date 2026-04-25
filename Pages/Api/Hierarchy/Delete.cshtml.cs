using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;
using System.Text.Json;

namespace ShiftManager.Pages.Api.Hierarchy;

/// <summary>
/// API endpoint to delete (soft-delete) hierarchy entities.
/// POST /Api/Hierarchy/Delete
/// </summary>
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:ManageHierarchy policy;
// hierarchy deletion needs cross-company child existence checks
[Authorize(Policy = "Grant:ManageHierarchy")]
[IgnoreAntiforgeryToken]
public class DeleteModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<DeleteModel> _logger;
    private readonly IStringLocalizer<SharedResources> _localizer;

    public DeleteModel(
        AppDbContext db,
        IAuditLogService auditLogService,
        ILogger<DeleteModel> logger,
        IStringLocalizer<SharedResources> localizer)
    {
        _db = db;
        _auditLogService = auditLogService;
        _logger = logger;
        _localizer = localizer;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            var request = JsonSerializer.Deserialize<DeleteRequest>(body, new JsonSerializerOptions
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

            string entityName;
            string result;

            switch (request.EntityType.ToLower())
            {
                case "project":
                    var project = await _db.Projects.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == request.EntityId);
                    if (project == null)
                    {
                        return new JsonResult(new { success = false, message = "Project not found" }) { StatusCode = 404 };
                    }
                    // Check for children
                    // MED-011 FIX: Only check active children (deactivated entities shouldn't block deletion)
                    var projectHasAreas = await _db.Areas.IgnoreQueryFilters().AnyAsync(a => a.ProjectId == request.EntityId && a.IsActive);
                    if (projectHasAreas)
                    {
                        return new JsonResult(new { success = false, message = "Cannot delete project with areas. Delete areas first." }) { StatusCode = 400 };
                    }
                    entityName = project.DisplayName;
                    project.IsActive = false;
                    result = $"Project '{entityName}' deactivated";
                    break;

                case "area":
                    var area = await _db.Areas.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == request.EntityId);
                    if (area == null)
                    {
                        return new JsonResult(new { success = false, message = "Area not found" }) { StatusCode = 404 };
                    }
                    // Check for children
                    // MED-011 FIX: Only check active children
                    var areaHasMolecules = await _db.Molecules.IgnoreQueryFilters().AnyAsync(m => m.AreaId == request.EntityId && m.IsActive);
                    if (areaHasMolecules)
                    {
                        return new JsonResult(new { success = false, message = "Cannot delete area with molecules. Delete molecules first." }) { StatusCode = 400 };
                    }
                    entityName = area.DisplayName;
                    area.IsActive = false;
                    result = $"Area '{entityName}' deactivated";
                    break;

                case "molecule":
                    var molecule = await _db.Molecules.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == request.EntityId);
                    if (molecule == null)
                    {
                        return new JsonResult(new { success = false, message = "Molecule not found" }) { StatusCode = 404 };
                    }
                    // Check for children
                    // MED-011 FIX: Only check active children
                    var moleculeHasCompanies = await _db.Companies.IgnoreQueryFilters().AnyAsync(c => c.MoleculeId == request.EntityId);
                    var moleculeHasDepartments = await _db.Departments.IgnoreQueryFilters().AnyAsync(d => d.MoleculeId == request.EntityId && d.IsActive);
                    if (moleculeHasCompanies || moleculeHasDepartments)
                    {
                        return new JsonResult(new { success = false, message = "Cannot delete molecule with companies or departments. Delete them first." }) { StatusCode = 400 };
                    }
                    entityName = molecule.DisplayName;
                    molecule.IsActive = false;
                    result = $"Molecule '{entityName}' deactivated";
                    break;

                case "company":
                    var company = await _db.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == request.EntityId);
                    if (company == null)
                    {
                        return new JsonResult(new { success = false, message = "Company not found" }) { StatusCode = 404 };
                    }
                    // Check for users
                    // MED-011 FIX: Only check active users
                    var companyHasUsers = await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.CompanyId == request.EntityId && u.IsActive);
                    if (companyHasUsers)
                    {
                        return new JsonResult(new { success = false, message = "Cannot delete company with users. Reassign or remove users first." }) { StatusCode = 400 };
                    }
                    entityName = company.DisplayName ?? company.Name;
                    // Clean up DirectorCompany mappings before removing the company
                    var directorMappings = await _db.DirectorCompanies
                        .Where(dc => dc.CompanyId == request.EntityId)
                        .ToListAsync();
                    if (directorMappings.Any())
                    {
                        _db.DirectorCompanies.RemoveRange(directorMappings);
                    }
                    // For companies, we actually remove since they don't have an IsActive flag
                    _db.Companies.Remove(company);
                    result = $"Company '{entityName}' deleted";
                    break;

                case "department":
                    var department = await _db.Departments.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Id == request.EntityId);
                    if (department == null)
                    {
                        return new JsonResult(new { success = false, message = "Department not found" }) { StatusCode = 404 };
                    }
                    // Check for users
                    // MED-011 FIX: Only check active users
                    var departmentHasUsers = await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.DepartmentId == request.EntityId && u.IsActive);
                    if (departmentHasUsers)
                    {
                        return new JsonResult(new { success = false, message = "Cannot delete department with users. Reassign or remove users first." }) { StatusCode = 400 };
                    }
                    entityName = department.DisplayName;
                    department.IsActive = false;
                    result = $"Department '{entityName}' deactivated";
                    break;

                default:
                    return new JsonResult(new { success = false, message = $"Invalid entity type: {request.EntityType}" }) { StatusCode = 400 };
            }

            await _db.SaveChangesAsync();

            _logger.LogInformation("Hierarchy delete: {Result}, UserId={UserId}", result, userId);

            await _auditLogService.LogAsync(
                action: "HierarchyEntityDeleted",
                entityType: request.EntityType,
                entityId: request.EntityId,
                description: result,
                details: JsonSerializer.Serialize(new
                {
                    EntityType = request.EntityType,
                    EntityId = request.EntityId,
                    Name = entityName,
                    UserId = userId
                }));

            return new JsonResult(new { success = true, message = result });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing hierarchy delete request");
            return new JsonResult(
                ShiftManager.Models.ApiErrorResponse.Create(
                    "ERROR_HIERARCHY_DELETE_FAILED",
                    _localizer["Error_HierarchyApi_DeleteFailed"].Value)
                .WithCorrelationId(HttpContext.TraceIdentifier))
            { StatusCode = 500 };
        }
    }

    private class DeleteRequest
    {
        public string EntityType { get; set; } = "";
        public int EntityId { get; set; }
    }
}
