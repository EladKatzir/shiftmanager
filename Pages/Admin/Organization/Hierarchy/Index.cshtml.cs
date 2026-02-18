using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;

namespace ShiftManager.Pages.Admin.Organization.Hierarchy;

/// <summary>
/// Hierarchy Overview - Visual tree structure of Project → Area → Molecule
/// Provides quick navigation and edit links for each level
/// </summary>
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:ViewHierarchy policy;
// hierarchy tree view is inherently cross-company organizational data
[Authorize(Policy = "Grant:ViewHierarchy")]
public class IndexModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        ILogger<IndexModel> logger) : base(localizer)
    {
        _db = db;
        _logger = logger;
    }

    // Hierarchy tree data
    public List<ProjectNode> HierarchyTree { get; set; } = new();

    // Stats
    public int TotalProjects { get; set; }
    public int TotalAreas { get; set; }
    public int TotalMolecules { get; set; }
    public int TotalCompanies { get; set; }

    public async Task OnGetAsync()
    {
        await LoadHierarchyTreeAsync();
    }

    private async Task LoadHierarchyTreeAsync()
    {
        // Load projects
        var projects = await _db.Projects
            .IgnoreQueryFilters()
            .OrderBy(p => p.Name)
            .ToListAsync();

        // Load areas
        var areas = await _db.Areas
            .IgnoreQueryFilters()
            .OrderBy(a => a.SortOrder).ThenBy(a => a.Name)
            .ToListAsync();

        // Load molecules
        var molecules = await _db.Molecules
            .IgnoreQueryFilters()
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Name)
            .ToListAsync();

        // Load companies
        var companies = await _db.Companies
            .IgnoreQueryFilters()
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync();

        // Build the tree
        HierarchyTree = projects.Select(p => new ProjectNode
        {
            Id = p.Id,
            Name = p.DisplayName,
            IsActive = p.IsActive,
            Areas = areas.Where(a => a.ProjectId == p.Id).Select(a => new AreaNode
            {
                Id = a.Id,
                Name = a.DisplayName,
                IsActive = a.IsActive,
                Molecules = molecules.Where(m => m.AreaId == a.Id).Select(m => new MoleculeNode
                {
                    Id = m.Id,
                    Name = m.DisplayName,
                    Type = m.Type,
                    IsActive = m.IsActive,
                    CompanyCount = companies.Count(c => c.MoleculeId == m.Id)
                }).ToList()
            }).ToList()
        }).ToList();

        // Calculate stats
        TotalProjects = projects.Count(p => p.IsActive);
        TotalAreas = areas.Count(a => a.IsActive);
        TotalMolecules = molecules.Count(m => m.IsActive);
        TotalCompanies = companies.Count;
    }

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

    public static string GetMoleculeTypeClass(MoleculeType type)
    {
        return type switch
        {
            MoleculeType.Workforce => "type-workforce",
            MoleculeType.Tech => "type-tech",
            MoleculeType.Helper => "type-helper",
            _ => "type-default"
        };
    }

    // Tree node classes
    public class ProjectNode
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public bool IsActive { get; set; }
        public List<AreaNode> Areas { get; set; } = new();
    }

    public class AreaNode
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public bool IsActive { get; set; }
        public List<MoleculeNode> Molecules { get; set; } = new();
    }

    public class MoleculeNode
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public MoleculeType Type { get; set; }
        public bool IsActive { get; set; }
        public int CompanyCount { get; set; }
    }
}
