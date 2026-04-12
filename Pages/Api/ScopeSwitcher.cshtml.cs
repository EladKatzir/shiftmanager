using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.Api;

/// <summary>
/// API endpoint to fetch available organizational scopes for the authenticated user.
/// Used by the Scope Switcher component to allow users to switch between scopes they have access to.
/// </summary>
[Authorize]
[IgnoreAntiforgeryToken]
public class ScopeSwitcherModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IGrantService _grantService;
    private readonly IHierarchyService _hierarchyService;
    private readonly ILogger<ScopeSwitcherModel> _logger;
    private readonly ICompanyCacheService _companyCacheService;

    public ScopeSwitcherModel(
        AppDbContext db,
        IGrantService grantService,
        IHierarchyService hierarchyService,
        ILogger<ScopeSwitcherModel> logger,
        ICompanyCacheService companyCacheService)
    {
        _db = db;
        _grantService = grantService;
        _hierarchyService = hierarchyService;
        _logger = logger;
        _companyCacheService = companyCacheService;
    }

    /// <summary>
    /// Gets available scopes for the current user.
    /// Returns the organizational hierarchy scopes the user has access to based on their grants.
    /// </summary>
    /// <param name="calendarType">Optional: the calendar type to check grants for (e.g., "shifts", "chores")</param>
    public async Task<IActionResult> OnGetAsync(string? calendarType = null)
    {
        try
        {
            Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
            Response.Headers["Pragma"] = "no-cache";

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            {
                _logger.LogWarning("ScopeSwitcher: Invalid user ID claim");
                return new JsonResult(new { error = "Invalid user session" }) { StatusCode = 401 };
            }

            var response = await GetUserScopesAsync(userId, calendarType);
            return new JsonResult(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ScopeSwitcher: Error fetching scopes for user");
            return StatusCode(500, new { error = "Error fetching available scopes" });
        }
    }

    /// <summary>
    /// Gets the full hierarchy tree with user access information.
    /// This is a more detailed endpoint that returns the complete org structure.
    /// </summary>
    public async Task<IActionResult> OnGetHierarchyAsync()
    {
        try
        {
            Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
            Response.Headers["Pragma"] = "no-cache";

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            {
                _logger.LogWarning("ScopeSwitcher: Invalid user ID claim");
                return new JsonResult(new { error = "Invalid user session" }) { StatusCode = 401 };
            }

            var response = await GetFullHierarchyAsync(userId);
            return new JsonResult(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ScopeSwitcher: Error fetching hierarchy for user");
            return StatusCode(500, new { error = "Error fetching organization hierarchy" });
        }
    }

    private async Task<ScopeSwitcherResponse> GetUserScopesAsync(int userId, string? calendarType)
    {
        var scopes = new List<ScopeDto>();
        var user = await _db.Users
            .Include(u => u.Department)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            _logger.LogWarning("ScopeSwitcher: User {UserId} not found", userId);
            return new ScopeSwitcherResponse { Scopes = scopes };
        }

        // Fetch the company if user has a company ID
        Company? company = null;
        if (user.CompanyId > 0)
        {
            company = await _companyCacheService.GetCompanyAsync(user.CompanyId);
        }

        // Get user's hierarchy context
        var userContext = await _hierarchyService.GetUserHierarchyContextAsync(userId);
        var isOwner = await _grantService.HasGrantAsync(userId, "AdminAccess");
        var isDirector = await _grantService.HasGrantAsync(userId, "DirectorHubAccess");

        // Always add "mine" scope - users can always see their own data
        scopes.Add(new ScopeDto
        {
            Type = "mine",
            Id = userId,
            Name = "My Shifts",
            ParentId = null,
            ParentType = null,
            IsDefault = false
        });

        // Add company scope(s) based on user's access level
        if (isDirector && userContext?.Path?.Molecule != null)
        {
            // Directors see all non-HQ companies in their molecule
            var moleculeCompanies = (await _db.Companies
                .Where(c => c.MoleculeId == userContext.Path.Molecule.Id && !c.IsHeadquarters)
                .ToListAsync())
                .OrderBy(c => c.LocalizedName, StringComparer.Create(System.Globalization.CultureInfo.CurrentUICulture, ignoreCase: true))
                .ToList();

            var isFirst = true;
            foreach (var molCompany in moleculeCompanies)
            {
                scopes.Add(new ScopeDto
                {
                    Type = "company",
                    Id = molCompany.Id,
                    Name = molCompany.LocalizedName,
                    ParentId = molCompany.MoleculeId,
                    ParentType = "molecule",
                    IsDefault = isFirst
                });
                isFirst = false;
            }
        }
        else if (user.CompanyId > 0 && company != null)
        {
            scopes.Add(new ScopeDto
            {
                Type = "company",
                Id = user.CompanyId,
                Name = company.LocalizedName,
                ParentId = company.MoleculeId,
                ParentType = "molecule",
                IsDefault = true
            });
        }

        // Add department scope if user belongs to a department (tech user)
        if (user.DepartmentId.HasValue && user.Department != null)
        {
            scopes.Add(new ScopeDto
            {
                Type = "department",
                Id = user.DepartmentId.Value,
                Name = user.Department.DisplayName ?? user.Department.Name,
                ParentId = user.Department.MoleculeId,
                ParentType = "molecule",
                IsDefault = true
            });
        }

        // Check for molecule-level access
        string grantKeyMolecule = string.IsNullOrEmpty(calendarType)
            ? "ViewShiftsMolecule"
            : $"View{ToTitleCase(calendarType)}Molecule";

        bool hasMoleculeGrant = await _grantService.HasGrantAsync(userId, grantKeyMolecule);
        if (hasMoleculeGrant || isDirector || isOwner)
        {
            if (userContext?.Path?.Molecule != null)
            {
                scopes.Add(new ScopeDto
                {
                    Type = "molecule",
                    Id = userContext.Path.Molecule.Id,
                    Name = userContext.Path.Molecule.DisplayName ?? userContext.Path.Molecule.Name,
                    ParentId = userContext.Path.Molecule.AreaId,
                    ParentType = "area",
                    IsDefault = false
                });
            }
        }

        // Check for area-level access
        string grantKeyArea = string.IsNullOrEmpty(calendarType)
            ? "ViewShiftsArea"
            : $"View{ToTitleCase(calendarType)}Area";

        bool hasAreaGrant = await _grantService.HasGrantAsync(userId, grantKeyArea);
        if (hasAreaGrant || isOwner)
        {
            if (userContext?.Path?.Area != null)
            {
                scopes.Add(new ScopeDto
                {
                    Type = "area",
                    Id = userContext.Path.Area.Id,
                    Name = userContext.Path.Area.DisplayName ?? userContext.Path.Area.Name,
                    ParentId = userContext.Path.Area.ProjectId,
                    ParentType = "project",
                    IsDefault = false
                });
            }
        }

        // Determine current scope - default to company if available, otherwise mine
        var currentScope = scopes.FirstOrDefault(s => s.IsDefault)
            ?? scopes.FirstOrDefault(s => s.Type == "company")
            ?? scopes.FirstOrDefault(s => s.Type == "department")
            ?? scopes.First();

        return new ScopeSwitcherResponse
        {
            Scopes = scopes,
            CurrentScope = new CurrentScopeDto
            {
                Type = currentScope.Type,
                Id = currentScope.Id
            },
            UserContext = new UserContextDto
            {
                UserId = userId,
                IsWorkforce = userContext?.IsWorkforce ?? (user.CompanyId > 0),
                IsTech = userContext?.IsTech ?? user.DepartmentId.HasValue,
                IsOwner = isOwner,
                IsDirector = isDirector
            }
        };
    }

    private async Task<HierarchyResponse> GetFullHierarchyAsync(int userId)
    {
        var isOwner = await _grantService.HasGrantAsync(userId, "AdminAccess");
        var isDirector = await _grantService.HasGrantAsync(userId, "DirectorHubAccess");

        // Get all projects (visible based on grants)
        var projects = new List<ProjectHierarchyDto>();

        // For Owner/Director, show all projects
        if (isOwner || isDirector)
        {
            var allProjects = await _db.Projects
                .Where(p => p.IsActive)
                .Include(p => p.Areas.Where(a => a.IsActive))
                .ThenInclude(a => a.Molecules.Where(m => m.IsActive))
                .ThenInclude(m => m.Companies)
                .Include(p => p.Areas.Where(a => a.IsActive))
                .ThenInclude(a => a.Molecules.Where(m => m.IsActive))
                .ThenInclude(m => m.Departments.Where(d => d.IsActive))
                .ToListAsync();

            foreach (var project in allProjects)
            {
                var projectDto = new ProjectHierarchyDto
                {
                    Id = project.Id,
                    Name = project.DisplayName ?? project.Name,
                    Areas = project.Areas.Select(area => new AreaHierarchyDto
                    {
                        Id = area.Id,
                        Name = area.DisplayName ?? area.Name,
                        Molecules = area.Molecules.Select(mol => new MoleculeHierarchyDto
                        {
                            Id = mol.Id,
                            Name = mol.DisplayName ?? mol.Name,
                            Type = mol.Type.ToString(),
                            Companies = mol.Companies
                                .OrderBy(c => c.LocalizedName, StringComparer.Create(System.Globalization.CultureInfo.CurrentUICulture, ignoreCase: true))
                                .Select(c => new CompanyHierarchyDto
                                {
                                    Id = c.Id,
                                    Name = c.LocalizedName
                                }).ToList(),
                            Departments = mol.Departments.Select(d => new DepartmentHierarchyDto
                            {
                                Id = d.Id,
                                Name = d.DisplayName ?? d.Name
                            }).ToList()
                        }).ToList()
                    }).ToList()
                };
                projects.Add(projectDto);
            }
        }
        else
        {
            // For regular users, show only their hierarchy path
            var userContext = await _hierarchyService.GetUserHierarchyContextAsync(userId);
            if (userContext?.Path != null)
            {
                var projectDto = new ProjectHierarchyDto
                {
                    Id = userContext.Path.Project.Id,
                    Name = userContext.Path.Project.DisplayName ?? userContext.Path.Project.Name,
                    Areas = new List<AreaHierarchyDto>
                    {
                        new AreaHierarchyDto
                        {
                            Id = userContext.Path.Area.Id,
                            Name = userContext.Path.Area.DisplayName ?? userContext.Path.Area.Name,
                            Molecules = new List<MoleculeHierarchyDto>
                            {
                                new MoleculeHierarchyDto
                                {
                                    Id = userContext.Path.Molecule.Id,
                                    Name = userContext.Path.Molecule.DisplayName ?? userContext.Path.Molecule.Name,
                                    Type = userContext.Path.Molecule.Type.ToString(),
                                    Companies = userContext.Path.Company != null
                                        ? new List<CompanyHierarchyDto>
                                        {
                                            new CompanyHierarchyDto
                                            {
                                                Id = userContext.Path.Company.Id,
                                                Name = userContext.Path.Company.LocalizedName
                                            }
                                        }
                                        : new List<CompanyHierarchyDto>(),
                                    Departments = userContext.Path.Department != null
                                        ? new List<DepartmentHierarchyDto>
                                        {
                                            new DepartmentHierarchyDto
                                            {
                                                Id = userContext.Path.Department.Id,
                                                Name = userContext.Path.Department.DisplayName ?? userContext.Path.Department.Name
                                            }
                                        }
                                        : new List<DepartmentHierarchyDto>()
                                }
                            }
                        }
                    }
                };
                projects.Add(projectDto);
            }
        }

        return new HierarchyResponse
        {
            Projects = projects,
            UserContext = new UserContextDto
            {
                UserId = userId,
                IsWorkforce = false, // Will be set properly if needed
                IsTech = false,
                IsOwner = isOwner,
                IsDirector = isDirector
            }
        };
    }

    private static string ToTitleCase(string str)
    {
        if (string.IsNullOrEmpty(str))
            return str;

        return char.ToUpper(str[0]) + str.Substring(1).ToLower();
    }
}

#region Response DTOs

/// <summary>
/// Response containing available scopes for the user
/// </summary>
public class ScopeSwitcherResponse
{
    public List<ScopeDto> Scopes { get; set; } = new();
    public CurrentScopeDto? CurrentScope { get; set; }
    public UserContextDto? UserContext { get; set; }
}

/// <summary>
/// A single organizational scope
/// </summary>
public class ScopeDto
{
    public string Type { get; set; } = string.Empty;
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? ParentId { get; set; }
    public string? ParentType { get; set; }
    public bool IsDefault { get; set; }
}

/// <summary>
/// Currently selected scope
/// </summary>
public class CurrentScopeDto
{
    public string Type { get; set; } = string.Empty;
    public int Id { get; set; }
}

/// <summary>
/// User's context information
/// </summary>
public class UserContextDto
{
    public int UserId { get; set; }
    public bool IsWorkforce { get; set; }
    public bool IsTech { get; set; }
    public bool IsOwner { get; set; }
    public bool IsDirector { get; set; }
}

/// <summary>
/// Full hierarchy response
/// </summary>
public class HierarchyResponse
{
    public List<ProjectHierarchyDto> Projects { get; set; } = new();
    public UserContextDto? UserContext { get; set; }
}

/// <summary>
/// Project in hierarchy
/// </summary>
public class ProjectHierarchyDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<AreaHierarchyDto> Areas { get; set; } = new();
}

/// <summary>
/// Area in hierarchy
/// </summary>
public class AreaHierarchyDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<MoleculeHierarchyDto> Molecules { get; set; } = new();
}

/// <summary>
/// Molecule in hierarchy
/// </summary>
public class MoleculeHierarchyDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public List<CompanyHierarchyDto> Companies { get; set; } = new();
    public List<DepartmentHierarchyDto> Departments { get; set; } = new();
}

/// <summary>
/// Company in hierarchy
/// </summary>
public class CompanyHierarchyDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Department in hierarchy
/// </summary>
public class DepartmentHierarchyDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

#endregion
