using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.ViewComponents;

/// <summary>
/// ViewComponent for the interactive Hierarchy Tree with drag-drop reordering.
/// Shows Project > Area > Molecule > Company/Department structure with
/// expand/collapse, inline editing, context menu, and drag-drop reordering.
/// </summary>
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — ViewComponent used only on grant-protected admin pages;
// hierarchy tree (Projects, Areas, Molecules, Companies, Departments) is inherently cross-company organizational data
public class HierarchyTreeViewComponent : ViewComponent
{
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly AppDbContext _db;
    private readonly IGrantService _grantService;
    private readonly ILogger<HierarchyTreeViewComponent> _logger;

    public HierarchyTreeViewComponent(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        IGrantService grantService,
        ILogger<HierarchyTreeViewComponent> logger)
    {
        _localizer = localizer;
        _db = db;
        _grantService = grantService;
        _logger = logger;
    }

    /// <summary>
    /// Invokes the Hierarchy Tree component.
    /// </summary>
    /// <param name="mode">Display mode: "full" (all levels) or "readonly" (no editing)</param>
    /// <param name="expandedByDefault">Whether nodes are expanded by default</param>
    public async Task<IViewComponentResult> InvokeAsync(
        string mode = "full",
        bool expandedByDefault = true)
    {
        var user = HttpContext.User;
        var userId = 0;
        var canEdit = false;
        var canReorder = false;
        var canDelete = false;
        var canAddChild = false;

        if (user.Identity?.IsAuthenticated == true)
        {
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userIdClaim) && int.TryParse(userIdClaim, out userId))
            {
                // Check grants for edit capabilities
                canEdit = await _grantService.HasGrantAsync(userId, "ManageHierarchy");
                canReorder = await _grantService.HasGrantAsync(userId, "ReorderHierarchy");
                canDelete = await _grantService.HasGrantAsync(userId, "ManageHierarchy");
                canAddChild = await _grantService.HasGrantAsync(userId, "ManageHierarchy");
            }
        }

        // If mode is readonly, disable all edit capabilities
        if (mode == "readonly")
        {
            canEdit = false;
            canReorder = false;
            canDelete = false;
            canAddChild = false;
        }

        // Load hierarchy data
        var tree = await LoadHierarchyTreeAsync();

        var model = new HierarchyTreeViewModel
        {
            Tree = tree,
            Mode = mode,
            ExpandedByDefault = expandedByDefault,
            CanEdit = canEdit,
            CanReorder = canReorder,
            CanDelete = canDelete,
            CanAddChild = canAddChild,
            Localizer = _localizer
        };

        return View(model);
    }

    private async Task<List<HierarchyProjectNode>> LoadHierarchyTreeAsync()
    {
        // Load all data with IgnoreQueryFilters to see full hierarchy
        var projects = await _db.Projects
            .IgnoreQueryFilters()
            .OrderBy(p => p.Name)
            .ToListAsync();

        var areas = await _db.Areas
            .IgnoreQueryFilters()
            .OrderBy(a => a.Name)
            .ToListAsync();

        var molecules = await _db.Molecules
            .IgnoreQueryFilters()
            .OrderBy(m => m.Name)
            .ToListAsync();

        var companies = (await _db.Companies
            .IgnoreQueryFilters()
            .ToListAsync())
            .OrderBy(c => c.LocalizedName, StringComparer.Create(System.Globalization.CultureInfo.CurrentUICulture, ignoreCase: true))
            .ToList();

        var departments = await _db.Departments
            .IgnoreQueryFilters()
            .OrderBy(d => d.Name)
            .ToListAsync();

        // Build the tree
        return projects.Select(p => new HierarchyProjectNode
        {
            Id = p.Id,
            Name = string.IsNullOrEmpty(p.DisplayName) ? p.Name : p.DisplayName,
            IsActive = p.IsActive,
            Areas = areas.Where(a => a.ProjectId == p.Id).Select(a => new HierarchyAreaNode
            {
                Id = a.Id,
                ProjectId = a.ProjectId,
                Name = string.IsNullOrEmpty(a.DisplayName) ? a.Name : a.DisplayName,
                IsActive = a.IsActive,
                Molecules = molecules.Where(m => m.AreaId == a.Id).Select(m => new HierarchyMoleculeNode
                {
                    Id = m.Id,
                    AreaId = m.AreaId,
                    Name = string.IsNullOrEmpty(m.DisplayName) ? m.Name : m.DisplayName,
                    Type = m.Type,
                    IsActive = m.IsActive,
                    Companies = companies.Where(c => c.MoleculeId == m.Id).Select(c => new HierarchyCompanyNode
                    {
                        Id = c.Id,
                        MoleculeId = m.Id,
                        Name = c.LocalizedName,
                        Slug = c.Slug
                    }).ToList(),
                    Departments = departments.Where(d => d.MoleculeId == m.Id).Select(d => new HierarchyDepartmentNode
                    {
                        Id = d.Id,
                        MoleculeId = m.Id,
                        Name = string.IsNullOrEmpty(d.DisplayName) ? d.Name : d.DisplayName,
                        IsActive = d.IsActive
                    }).ToList()
                }).ToList()
            }).ToList()
        }).ToList();
    }
}

#region View Models

public class HierarchyTreeViewModel
{
    public List<HierarchyProjectNode> Tree { get; set; } = new();
    public string Mode { get; set; } = "full";
    public bool ExpandedByDefault { get; set; } = true;
    public bool CanEdit { get; set; }
    public bool CanReorder { get; set; }
    public bool CanDelete { get; set; }
    public bool CanAddChild { get; set; }
    public IStringLocalizer<SharedResources> Localizer { get; set; } = null!;
}

public class HierarchyProjectNode
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }
    public List<HierarchyAreaNode> Areas { get; set; } = new();
}

public class HierarchyAreaNode
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }
    public List<HierarchyMoleculeNode> Molecules { get; set; } = new();
}

public class HierarchyMoleculeNode
{
    public int Id { get; set; }
    public int AreaId { get; set; }
    public string Name { get; set; } = "";
    public MoleculeType Type { get; set; }
    public bool IsActive { get; set; }
    public List<HierarchyCompanyNode> Companies { get; set; } = new();
    public List<HierarchyDepartmentNode> Departments { get; set; } = new();

    public bool HasChildren => Companies.Any() || Departments.Any();
}

public class HierarchyCompanyNode
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public string Name { get; set; } = "";
    public string? Slug { get; set; }
}

public class HierarchyDepartmentNode
{
    public int Id { get; set; }
    public int MoleculeId { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }
}

/// <summary>
/// ViewModel for individual node partial views
/// </summary>
public class HierarchyNodeViewModel
{
    public HierarchyProjectNode? Project { get; set; }
    public HierarchyAreaNode? Area { get; set; }
    public HierarchyMoleculeNode? Molecule { get; set; }
    public bool CanEdit { get; set; }
    public bool CanReorder { get; set; }
    public bool CanDelete { get; set; }
    public bool CanAddChild { get; set; }
    public bool ExpandedByDefault { get; set; }
}

#endregion
