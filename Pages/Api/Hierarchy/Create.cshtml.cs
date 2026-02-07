using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using System.Security.Claims;
using System.Text.Json;

namespace ShiftManager.Pages.Api.Hierarchy;

/// <summary>
/// API endpoint to create new hierarchy entities.
/// POST /Api/Hierarchy/Create
/// </summary>
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:CreateHierarchy policy;
// hierarchy entity creation needs cross-company parent lookups
[Authorize(Policy = "Grant:CreateHierarchy")]
[IgnoreAntiforgeryToken]
public class CreateModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<CreateModel> _logger;

    public CreateModel(
        AppDbContext db,
        IAuditLogService auditLogService,
        ILogger<CreateModel> logger)
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
            var request = JsonSerializer.Deserialize<CreateRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (request == null || string.IsNullOrEmpty(request.EntityType) || string.IsNullOrWhiteSpace(request.Name))
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

            int entityId;
            string result;

            switch (request.EntityType.ToLower())
            {
                case "project":
                    var project = new Project
                    {
                        Name = GenerateSlug(request.Name),
                        DisplayName = request.Name.Trim(),
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    _db.Projects.Add(project);
                    await _db.SaveChangesAsync();
                    entityId = project.Id;
                    result = $"Project '{request.Name}' created with ID {entityId}";
                    break;

                case "area":
                    if (!request.ParentId.HasValue)
                    {
                        return new JsonResult(new { success = false, message = "Parent project ID required" }) { StatusCode = 400 };
                    }
                    var parentProject = await _db.Projects.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == request.ParentId);
                    if (parentProject == null)
                    {
                        return new JsonResult(new { success = false, message = "Parent project not found" }) { StatusCode = 404 };
                    }
                    var area = new Area
                    {
                        ProjectId = request.ParentId.Value,
                        Name = GenerateSlug(request.Name),
                        DisplayName = request.Name.Trim(),
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    _db.Areas.Add(area);
                    await _db.SaveChangesAsync();
                    entityId = area.Id;
                    result = $"Area '{request.Name}' created with ID {entityId} under project {request.ParentId}";
                    break;

                case "molecule":
                    if (!request.ParentId.HasValue)
                    {
                        return new JsonResult(new { success = false, message = "Parent area ID required" }) { StatusCode = 400 };
                    }
                    var parentArea = await _db.Areas.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == request.ParentId);
                    if (parentArea == null)
                    {
                        return new JsonResult(new { success = false, message = "Parent area not found" }) { StatusCode = 404 };
                    }
                    var molecule = new Molecule
                    {
                        AreaId = request.ParentId.Value,
                        Name = GenerateSlug(request.Name),
                        DisplayName = request.Name.Trim(),
                        Type = MoleculeType.Workforce, // Default type
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    _db.Molecules.Add(molecule);
                    await _db.SaveChangesAsync();
                    entityId = molecule.Id;
                    result = $"Molecule '{request.Name}' created with ID {entityId} under area {request.ParentId}";
                    break;

                case "company":
                    if (!request.ParentId.HasValue)
                    {
                        return new JsonResult(new { success = false, message = "Parent molecule ID required" }) { StatusCode = 400 };
                    }
                    var parentMolecule = await _db.Molecules.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == request.ParentId);
                    if (parentMolecule == null)
                    {
                        return new JsonResult(new { success = false, message = "Parent molecule not found" }) { StatusCode = 404 };
                    }
                    var company = new Company
                    {
                        MoleculeId = request.ParentId.Value,
                        Name = GenerateSlug(request.Name),
                        DisplayName = request.Name.Trim(),
                        Slug = GenerateSlug(request.Name)
                    };
                    _db.Companies.Add(company);
                    await _db.SaveChangesAsync();
                    entityId = company.Id;
                    result = $"Company '{request.Name}' created with ID {entityId} under molecule {request.ParentId}";
                    break;

                case "department":
                    if (!request.ParentId.HasValue)
                    {
                        return new JsonResult(new { success = false, message = "Parent molecule ID required" }) { StatusCode = 400 };
                    }
                    var parentMoleculeForDept = await _db.Molecules.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == request.ParentId);
                    if (parentMoleculeForDept == null)
                    {
                        return new JsonResult(new { success = false, message = "Parent molecule not found" }) { StatusCode = 404 };
                    }
                    var department = new Department
                    {
                        MoleculeId = request.ParentId.Value,
                        Name = GenerateSlug(request.Name),
                        DisplayName = request.Name.Trim(),
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    _db.Departments.Add(department);
                    await _db.SaveChangesAsync();
                    entityId = department.Id;
                    result = $"Department '{request.Name}' created with ID {entityId} under molecule {request.ParentId}";
                    break;

                default:
                    return new JsonResult(new { success = false, message = $"Invalid entity type: {request.EntityType}" }) { StatusCode = 400 };
            }

            _logger.LogInformation("Hierarchy create: {Result}, UserId={UserId}", result, userId);

            await _auditLogService.LogAsync(
                action: "HierarchyEntityCreated",
                entityType: request.EntityType,
                entityId: entityId,
                description: result,
                details: JsonSerializer.Serialize(new
                {
                    EntityType = request.EntityType,
                    EntityId = entityId,
                    Name = request.Name,
                    ParentId = request.ParentId,
                    UserId = userId
                }));

            return new JsonResult(new { success = true, entityId = entityId, message = result });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing hierarchy create request");
            return new JsonResult(new { success = false, message = "An error occurred" }) { StatusCode = 500 };
        }
    }

    private static string GenerateSlug(string name)
    {
        return name.Trim()
            .ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("_", "-");
    }

    private class CreateRequest
    {
        public string EntityType { get; set; } = "";
        public string Name { get; set; } = "";
        public int? ParentId { get; set; }
    }
}
