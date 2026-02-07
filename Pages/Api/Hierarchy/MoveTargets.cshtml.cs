using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using System.Security.Claims;

namespace ShiftManager.Pages.Api.Hierarchy;

/// <summary>
/// API endpoint to get valid move targets for a hierarchy entity.
/// GET /Api/Hierarchy/MoveTargets?entityType=area&amp;entityId=1
/// </summary>
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:ReorderHierarchy policy;
// move targets needs cross-company hierarchy lookups to find valid parents
[Authorize(Policy = "Grant:ReorderHierarchy")]
public class MoveTargetsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<MoveTargetsModel> _logger;

    public MoveTargetsModel(
        AppDbContext db,
        ILogger<MoveTargetsModel> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IActionResult> OnGetAsync(string entityType, int entityId)
    {
        try
        {
            if (string.IsNullOrEmpty(entityType) || entityId <= 0)
            {
                return new JsonResult(new { success = false, message = "Invalid request data", targets = new List<object>() }) { StatusCode = 400 };
            }

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            {
                return new JsonResult(new { success = false, message = "User not authenticated", targets = new List<object>() }) { StatusCode = 401 };
            }

            List<MoveTarget> targets;

            switch (entityType.ToLower())
            {
                case "area":
                    // Areas can move to other projects
                    var area = await _db.Areas.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == entityId);
                    if (area == null)
                    {
                        return new JsonResult(new { success = false, message = "Area not found", targets = new List<object>() }) { StatusCode = 404 };
                    }
                    targets = await _db.Projects
                        .IgnoreQueryFilters()
                        .Where(p => p.IsActive && p.Id != area.ProjectId)
                        .Select(p => new MoveTarget { Id = p.Id, Name = p.DisplayName })
                        .ToListAsync();
                    break;

                case "molecule":
                    // Molecules can move to other areas
                    var molecule = await _db.Molecules.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == entityId);
                    if (molecule == null)
                    {
                        return new JsonResult(new { success = false, message = "Molecule not found", targets = new List<object>() }) { StatusCode = 404 };
                    }
                    targets = await _db.Areas
                        .IgnoreQueryFilters()
                        .Where(a => a.IsActive && a.Id != molecule.AreaId)
                        .Select(a => new MoveTarget { Id = a.Id, Name = a.DisplayName })
                        .ToListAsync();
                    break;

                case "company":
                    // Companies can move to other molecules
                    var company = await _db.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == entityId);
                    if (company == null)
                    {
                        return new JsonResult(new { success = false, message = "Company not found", targets = new List<object>() }) { StatusCode = 404 };
                    }
                    targets = await _db.Molecules
                        .IgnoreQueryFilters()
                        .Where(m => m.IsActive && m.Id != company.MoleculeId)
                        .Select(m => new MoveTarget { Id = m.Id, Name = m.DisplayName })
                        .ToListAsync();
                    break;

                case "department":
                    // Departments can move to other molecules
                    var department = await _db.Departments.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Id == entityId);
                    if (department == null)
                    {
                        return new JsonResult(new { success = false, message = "Department not found", targets = new List<object>() }) { StatusCode = 404 };
                    }
                    targets = await _db.Molecules
                        .IgnoreQueryFilters()
                        .Where(m => m.IsActive && m.Id != department.MoleculeId)
                        .Select(m => new MoveTarget { Id = m.Id, Name = m.DisplayName })
                        .ToListAsync();
                    break;

                default:
                    // Projects cannot be moved (they are top-level)
                    targets = new List<MoveTarget>();
                    break;
            }

            return new JsonResult(new { success = true, targets = targets });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting move targets for {EntityType} {EntityId}", entityType, entityId);
            return new JsonResult(new { success = false, message = "An error occurred", targets = new List<object>() }) { StatusCode = 500 };
        }
    }

    private class MoveTarget
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }
}
