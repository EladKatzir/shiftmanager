using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Pages.Owner.Hub;

// SECURITY-AUDITED: IgnoreQueryFilters() in this class is SAFE — Owner diagnostic tool gated by Grant:AdminAccess
[Authorize(Policy = "Grant:AdminAccess")]
public class PermissionSimulatorModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IPermissionSimulatorService _simulator;

    public PermissionSimulatorModel(AppDbContext db, IPermissionSimulatorService simulator)
    {
        _db = db;
        _simulator = simulator;
    }

    public List<GrantTypeGroup> GrantTypeGroups { get; set; } = new();
    public List<RoleTemplateItem> RoleTemplates  { get; set; } = new();
    public List<ProjectItem>      Projects       { get; set; } = new();

    public async Task OnGetAsync()
    {
        // IgnoreQueryFilters: owner-only diagnostic tool, cross-tenant reads are intentional
        var grantTypes = await _db.GrantTypes
            .IgnoreQueryFilters()
            .Where(gt => gt.IsActive)
            .OrderBy(gt => gt.Category)
            .ThenBy(gt => gt.Key)
            .Select(gt => new { gt.Id, gt.Key, gt.NameKey, gt.Category })
            .ToListAsync();

        GrantTypeGroups = grantTypes
            .GroupBy(gt => gt.Category)
            .Select(g => new GrantTypeGroup
            {
                Category = g.Key.ToString(),
                Items    = g.Select(gt => new GrantTypeItem { Id = gt.Id, Key = gt.Key, NameKey = gt.NameKey }).ToList()
            })
            .ToList();

        // IgnoreQueryFilters: owner-only diagnostic tool, cross-tenant reads are intentional
        RoleTemplates = await _db.RoleTemplates
            .IgnoreQueryFilters()
            .Where(rt => rt.IsActive)
            .OrderBy(rt => rt.Key)
            .Select(rt => new RoleTemplateItem { Id = rt.Id, Key = rt.Key, NameKey = rt.NameKey })
            .ToListAsync();

        // IgnoreQueryFilters: owner-only diagnostic tool, cross-tenant reads are intentional
        Projects = await _db.Projects
            .IgnoreQueryFilters()
            .OrderBy(p => p.Name)
            .Select(p => new ProjectItem { Id = p.Id, Name = p.Name })
            .ToListAsync();
    }

    // User typeahead — cross-tenant search for owner
    public async Task<IActionResult> OnGetSearchUsersAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return new JsonResult(Array.Empty<object>());

        // IgnoreQueryFilters: owner-only diagnostic tool, cross-tenant reads are intentional
        var users = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.IsActive && (u.DisplayName.Contains(query) || u.Email.Contains(query)))
            .OrderBy(u => u.DisplayName)
            .Take(20)
            .Select(u => new { u.Id, u.DisplayName, u.Email, u.CompanyId })
            .ToListAsync();

        var companyIds = users.Select(u => u.CompanyId).Distinct().ToList();
        // IgnoreQueryFilters: owner-only diagnostic tool, cross-tenant reads are intentional
        var companies = await _db.Companies
            .IgnoreQueryFilters()
            .Where(c => companyIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name);

        var result = users.Select(u => new
        {
            u.Id,
            u.DisplayName,
            u.Email,
            CompanyName = companies.GetValueOrDefault(u.CompanyId)
        });

        return new JsonResult(result);
    }

    // Hierarchy cascade — children for a given scope level
    public async Task<IActionResult> OnGetHierarchyChildrenAsync(string level, int parentId)
    {
        // IgnoreQueryFilters: owner-only diagnostic tool, cross-tenant reads are intentional
        IEnumerable<object> items = level switch
        {
            "area" => await _db.Areas.IgnoreQueryFilters()
                .Where(a => a.ProjectId == parentId)
                .OrderBy(a => a.Name)
                .Select(a => new { a.Id, a.Name })
                .ToListAsync<object>(),

            "molecule" => await _db.Molecules.IgnoreQueryFilters()
                .Where(m => m.AreaId == parentId)
                .OrderBy(m => m.Name)
                .Select(m => new { m.Id, m.Name })
                .ToListAsync<object>(),

            "company" => await _db.Companies.IgnoreQueryFilters()
                .Where(c => c.MoleculeId == parentId)
                .OrderBy(c => c.Name)
                .Select(c => new { c.Id, c.Name })
                .ToListAsync<object>(),

            "jobtype" => await _db.JobTypes.IgnoreQueryFilters()
                .Where(j => j.AreaId == parentId && j.IsActive)
                .OrderBy(j => j.SortOrder)
                .Select(j => new { j.Id, Name = j.DisplayName != string.Empty ? j.DisplayName : j.Name })
                .ToListAsync<object>(),

            _ => Array.Empty<object>()
        };

        return new JsonResult(items);
    }

    // User hierarchy context — for the user card trail
    public async Task<IActionResult> OnGetUserContextAsync(int userId)
    {
        // IgnoreQueryFilters: owner-only diagnostic tool, cross-tenant reads are intentional
        var user = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.DisplayName, u.Email, u.CompanyId, u.JobTypeId })
            .FirstOrDefaultAsync();

        if (user == null)
            return new JsonResult(new { error = "User not found" }) { StatusCode = 404 };

        var companyName = user.CompanyId > 0
            ? (await _db.Companies.IgnoreQueryFilters()
                .Where(c => c.Id == user.CompanyId)
                .Select(c => new { c.Name, c.MoleculeId })
                .FirstOrDefaultAsync())
            : null;

        int? moleculeId  = companyName != null ? (int?)companyName.MoleculeId : null;
        int? areaId      = null;
        int? projectId   = null;
        string? moleculeName = null;
        string? areaName     = null;
        string? projectName  = null;

        if (moleculeId.HasValue)
        {
            var mol = await _db.Molecules.IgnoreQueryFilters()
                .Where(m => m.Id == moleculeId.Value)
                .Select(m => new { m.Name, m.AreaId })
                .FirstOrDefaultAsync();
            if (mol != null)
            {
                moleculeName = mol.Name;
                areaId = mol.AreaId;
                var area = await _db.Areas.IgnoreQueryFilters()
                    .Where(a => a.Id == areaId.Value)
                    .Select(a => new { a.Name, a.ProjectId })
                    .FirstOrDefaultAsync();
                if (area != null)
                {
                    areaName  = area.Name;
                    projectId = area.ProjectId;
                    var project = await _db.Projects.IgnoreQueryFilters()
                        .Where(p => p.Id == projectId.Value)
                        .Select(p => new { p.Name })
                        .FirstOrDefaultAsync();
                    projectName = project?.Name;
                }
            }
        }

        string? jobTypeName = null;
        if (user.JobTypeId.HasValue)
        {
            jobTypeName = await _db.JobTypes.IgnoreQueryFilters()
                .Where(j => j.Id == user.JobTypeId.Value)
                .Select(j => j.DisplayName != string.Empty ? j.DisplayName : j.Name)
                .FirstOrDefaultAsync();
        }

        return new JsonResult(new
        {
            userId      = user.Id,
            displayName = user.DisplayName,
            email       = user.Email,
            projectId, projectName,
            areaId, areaName,
            moleculeId, moleculeName,
            companyId   = user.CompanyId,
            companyName = companyName?.Name,
            jobTypeId   = user.JobTypeId,
            jobTypeName
        });
    }

    public async Task<IActionResult> OnPostSimulateAsync([FromBody] SimulationRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.GrantKey))
                return new JsonResult(new { error = "Grant key is required" }) { StatusCode = 400 };

            var result = request.IsRoleMode
                ? await _simulator.SimulateRoleAccessAsync(
                    request.RoleTemplateId ?? 0,
                    request.SimulatedScopeJobTypeId,
                    request.SimulatedScopeCompanyId,
                    request.SimulatedScopeMoleculeId,
                    request.SimulatedScopeAreaId,
                    request.GrantKey,
                    request.TargetProjectId,
                    request.TargetAreaId,
                    request.TargetMoleculeId,
                    request.TargetDepartmentId,
                    request.TargetCompanyId,
                    request.TargetJobTypeId)
                : await _simulator.SimulateUserAccessAsync(
                    request.UserId ?? 0,
                    request.GrantKey,
                    request.TargetProjectId,
                    request.TargetAreaId,
                    request.TargetMoleculeId,
                    request.TargetDepartmentId,
                    request.TargetCompanyId,
                    request.TargetJobTypeId,
                    request.TargetUserId);

            return new JsonResult(result);
        }
        catch (Exception ex)
        {
            return new JsonResult(new { error = ex.Message }) { StatusCode = 500 };
        }
    }
}

public record SimulationRequest(
    bool   IsRoleMode,
    int?   UserId,
    int?   RoleTemplateId,
    int?   SimulatedScopeJobTypeId,
    int?   SimulatedScopeCompanyId,
    int?   SimulatedScopeMoleculeId,
    int?   SimulatedScopeAreaId,
    string GrantKey,
    int?   TargetProjectId,
    int?   TargetAreaId,
    int?   TargetMoleculeId,
    int?   TargetDepartmentId,
    int?   TargetCompanyId,
    int?   TargetJobTypeId,
    int?   TargetUserId
);

public class GrantTypeGroup
{
    public string          Category { get; set; } = "";
    public List<GrantTypeItem> Items { get; set; } = new();
}

public class GrantTypeItem
{
    public int    Id      { get; set; }
    public string Key     { get; set; } = "";
    public string NameKey { get; set; } = "";
}

public class RoleTemplateItem
{
    public int    Id      { get; set; }
    public string Key     { get; set; } = "";
    public string NameKey { get; set; } = "";
}

public class ProjectItem
{
    public int    Id   { get; set; }
    public string Name { get; set; } = "";
}
