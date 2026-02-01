using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Pages.Admin.Organization;

[Authorize(Policy = "Grant:ViewHierarchy")]
public class IndexModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly IHierarchyService _hierarchyService;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        IHierarchyService hierarchyService,
        ILogger<IndexModel> logger) : base(localizer)
    {
        _db = db;
        _hierarchyService = hierarchyService;
        _logger = logger;
    }

    // View Models
    public record ProjectVM(int Id, string Name, string DisplayName, bool IsActive, int AreaCount);
    public record AreaVM(int Id, int ProjectId, string Name, string DisplayName, bool IsActive, int MoleculeCount, int JobTypeCount);
    public record MoleculeVM(int Id, int AreaId, string Name, string DisplayName, MoleculeType Type, bool IsActive, int CompanyCount, int DepartmentCount);
    public record CompanyVM(int Id, int? MoleculeId, string Name, string? DisplayName, int UserCount);
    public record DepartmentVM(int Id, int MoleculeId, string Name, string DisplayName, bool IsActive, int UserCount);

    // Data
    public List<ProjectVM> Projects { get; set; } = new();
    public List<AreaVM> Areas { get; set; } = new();
    public List<MoleculeVM> Molecules { get; set; } = new();
    public List<CompanyVM> Companies { get; set; } = new();
    public List<DepartmentVM> Departments { get; set; } = new();

    // Stats
    public int TotalUsers { get; set; }
    public int TotalActiveProjects { get; set; }
    public int TotalActiveAreas { get; set; }
    public int TotalActiveMolecules { get; set; }

    public async Task OnGetAsync()
    {
        // Load all hierarchy data
        await LoadHierarchyDataAsync();
    }

    private async Task LoadHierarchyDataAsync()
    {
        // Load projects with area counts
        Projects = await _db.Projects
            .IgnoreQueryFilters()
            .Select(p => new ProjectVM(
                p.Id,
                p.Name,
                p.DisplayName,
                p.IsActive,
                p.Areas.Count(a => a.IsActive)
            ))
            .OrderBy(p => p.Name)
            .ToListAsync();

        // Load areas with molecule and job type counts
        Areas = await _db.Areas
            .IgnoreQueryFilters()
            .Select(a => new AreaVM(
                a.Id,
                a.ProjectId,
                a.Name,
                a.DisplayName,
                a.IsActive,
                a.Molecules.Count(m => m.IsActive),
                a.JobTypes.Count(jt => jt.IsActive)
            ))
            .OrderBy(a => a.Name)
            .ToListAsync();

        // Load molecules with company/department counts
        Molecules = await _db.Molecules
            .IgnoreQueryFilters()
            .Select(m => new MoleculeVM(
                m.Id,
                m.AreaId,
                m.Name,
                m.DisplayName,
                m.Type,
                m.IsActive,
                m.Companies.Count,
                m.Departments.Count(d => d.IsActive)
            ))
            .OrderBy(m => m.Name)
            .ToListAsync();

        // Load companies with user counts
        var userCountsByCompany = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.IsActive)
            .GroupBy(u => u.CompanyId)
            .Select(g => new { CompanyId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CompanyId, x => x.Count);

        Companies = await _db.Companies
            .IgnoreQueryFilters()
            .Select(c => new CompanyVM(
                c.Id,
                c.MoleculeId,
                c.Name,
                c.DisplayName,
                userCountsByCompany.GetValueOrDefault(c.Id, 0)
            ))
            .OrderBy(c => c.Name)
            .ToListAsync();

        // Load departments with user counts
        var userCountsByDept = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.IsActive && u.DepartmentId.HasValue)
            .GroupBy(u => u.DepartmentId!.Value)
            .Select(g => new { DepartmentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DepartmentId, x => x.Count);

        Departments = await _db.Departments
            .IgnoreQueryFilters()
            .Select(d => new DepartmentVM(
                d.Id,
                d.MoleculeId,
                d.Name,
                d.DisplayName,
                d.IsActive,
                userCountsByDept.GetValueOrDefault(d.Id, 0)
            ))
            .OrderBy(d => d.Name)
            .ToListAsync();

        // Calculate stats
        TotalUsers = await _db.Users.IgnoreQueryFilters().CountAsync(u => u.IsActive);
        TotalActiveProjects = Projects.Count(p => p.IsActive);
        TotalActiveAreas = Areas.Count(a => a.IsActive);
        TotalActiveMolecules = Molecules.Count(m => m.IsActive);
    }

    // Helper method to get molecule type display name
    public string GetMoleculeTypeDisplay(MoleculeType type)
    {
        return type switch
        {
            MoleculeType.Workforce => _localizer["Workforce"],
            MoleculeType.Tech => _localizer["Tech"],
            MoleculeType.Helper => _localizer["Helper"],
            _ => type.ToString()
        };
    }

    // Helper method to get molecule type badge class
    public static string GetMoleculeTypeBadgeClass(MoleculeType type)
    {
        return type switch
        {
            MoleculeType.Workforce => "badge-workforce",
            MoleculeType.Tech => "badge-tech",
            MoleculeType.Helper => "badge-helper",
            _ => "badge-default"
        };
    }
}
