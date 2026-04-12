using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Helpers;
using ShiftManager.Services;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace ShiftManager.Pages.Admin.Organization;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:ViewHierarchy policy;
// organization hierarchy overview is inherently cross-company reference data
[Authorize(Policy = "Grant:ViewHierarchy")]
public class IndexModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly IHierarchyService _hierarchyService;
    private readonly ILogger<IndexModel> _logger;
    private readonly ICompanyCacheService _companyCacheService;
    private readonly IGrantService _grantService;

    public IndexModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        IHierarchyService hierarchyService,
        ILogger<IndexModel> logger,
        ICompanyCacheService companyCacheService,
        IGrantService grantService) : base(localizer)
    {
        _db = db;
        _hierarchyService = hierarchyService;
        _logger = logger;
        _companyCacheService = companyCacheService;
        _grantService = grantService;
    }

    // View Models
    public record ProjectVM(int Id, string Name, string DisplayName, bool IsActive, int AreaCount);
    public record AreaVM(int Id, int ProjectId, string Name, string DisplayName, bool IsActive, int MoleculeCount, int JobTypeCount);
    public record MoleculeVM(int Id, int AreaId, string Name, string DisplayName, MoleculeType Type, bool IsActive, int CompanyCount, int DepartmentCount);
    public record CompanyVM(int Id, int? MoleculeId, string Name, string? DisplayName, string? NameHe, int UserCount)
    {
        public string LocalizedName => Company.ResolveLocalizedName(Name, DisplayName, NameHe);
    }
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

    /// <summary>Whether the current user has ManageHierarchy grant (can add/rename/delete).</summary>
    public bool CanManageHierarchy { get; set; }

    public async Task OnGetAsync()
    {
        CanManageHierarchy = await CurrentUserHasManageHierarchyAsync();
        // Load all hierarchy data
        await LoadHierarchyDataAsync();
    }

    private int? GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : null;
    }

    /// <summary>Unscoped check — used for UI (show/hide buttons). Any ManageHierarchy grant = show buttons.</summary>
    private async Task<bool> CurrentUserHasManageHierarchyAsync()
    {
        var userId = GetCurrentUserId();
        return userId.HasValue && await _grantService.HasGrantAsync(userId.Value, "ManageHierarchy");
    }

    /// <summary>Scoped check — used by POST handlers. Verifies the grant covers the target molecule.</summary>
    private async Task<bool> CurrentUserCanManageMoleculeAsync(int moleculeId)
    {
        var userId = GetCurrentUserId();
        return userId.HasValue && await _grantService.HasGrantWithScopeAsync(
            userId.Value, "ManageHierarchy", moleculeId: moleculeId);
    }

    private async Task LoadHierarchyDataAsync()
    {
        // Load projects with area counts
        Projects = await _db.Projects
            .IgnoreQueryFilters()
            .OrderBy(p => p.Name)
            .Select(p => new ProjectVM(
                p.Id,
                p.Name,
                p.DisplayName,
                p.IsActive,
                p.Areas.Count(a => a.IsActive)
            ))
            .ToListAsync();

        // Load areas with molecule and job type counts
        Areas = await _db.Areas
            .IgnoreQueryFilters()
            .OrderBy(a => a.Name)
            .Select(a => new AreaVM(
                a.Id,
                a.ProjectId,
                a.Name,
                a.DisplayName,
                a.IsActive,
                a.Molecules.Count(m => m.IsActive),
                a.JobTypes.Count(jt => jt.IsActive)
            ))
            .ToListAsync();

        // Load molecules with company/department counts
        Molecules = await _db.Molecules
            .IgnoreQueryFilters()
            .OrderBy(m => m.Name)
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
            .ToListAsync();

        // Load companies with user counts
        var userCountsByCompany = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.IsActive)
            .GroupBy(u => u.CompanyId)
            .Select(g => new { CompanyId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CompanyId, x => x.Count);

        var companiesRaw = await _db.Companies
            .IgnoreQueryFilters()
            .ToListAsync();

        var cultureComparer = StringComparer.Create(System.Globalization.CultureInfo.CurrentUICulture, ignoreCase: true);
        Companies = companiesRaw
            .Select(c => new CompanyVM(
                c.Id,
                c.MoleculeId,
                c.Name,
                c.DisplayName,
                c.NameHe,
                userCountsByCompany.GetValueOrDefault(c.Id, 0)
            ))
            .OrderBy(c => c.LocalizedName, cultureComparer)
            .ToList();

        // Load departments with user counts
        var userCountsByDept = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.IsActive && u.DepartmentId.HasValue)
            .GroupBy(u => u.DepartmentId!.Value)
            .Select(g => new { DepartmentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DepartmentId, x => x.Count);

        var deptsRaw = await _db.Departments
            .IgnoreQueryFilters()
            .OrderBy(d => d.Name)
            .ToListAsync();

        Departments = deptsRaw
            .Select(d => new DepartmentVM(
                d.Id,
                d.MoleculeId,
                d.Name,
                d.DisplayName,
                d.IsActive,
                userCountsByDept.GetValueOrDefault(d.Id, 0)
            ))
            .ToList();

        // Calculate stats
        TotalUsers = await _db.Users.IgnoreQueryFilters().CountAsync(u => u.IsActive);
        TotalActiveProjects = Projects.Count(p => p.IsActive);
        TotalActiveAreas = Areas.Count(a => a.IsActive);
        TotalActiveMolecules = Molecules.Count(m => m.IsActive);
    }

    // Bind properties for inline CRUD forms
    [BindProperty] public int EntityId { get; set; }
    [BindProperty] public string EntityName { get; set; } = string.Empty;
    [BindProperty] public string EntityDisplayName { get; set; } = string.Empty;
    [BindProperty] public int ParentMoleculeId { get; set; }

    // ================================================================
    // Company CRUD handlers
    // ================================================================

    public async Task<IActionResult> OnPostAddCompanyAsync()
    {
        if (ParentMoleculeId <= 0 || !await CurrentUserCanManageMoleculeAsync(ParentMoleculeId))
            return Forbid();

        if (string.IsNullOrWhiteSpace(EntityName) || ParentMoleculeId <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_RequiredFields"].Value;
            return RedirectToPage();
        }

        if (ContainsDangerousContent(EntityName) || ContainsDangerousContent(EntityDisplayName))
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidInput"].Value;
            return RedirectToPage();
        }

        var molecule = await _db.Molecules.FirstOrDefaultAsync(m => m.Id == ParentMoleculeId);
        if (molecule == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_MoleculeNotFound"].Value;
            return RedirectToPage();
        }

        // Companies should only be added to Workforce or Helper molecules, not Tech
        if (molecule.Type == MoleculeType.Tech)
        {
            TempData["ErrorMessage"] = _localizer["Error_CannotAddCompanyToTechMolecule"].Value;
            return RedirectToPage();
        }

        var slug = GenerateSlug(EntityName);
        var company = new Company
        {
            Name = EntityName.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(EntityDisplayName) ? null : EntityDisplayName.Trim(),
            Slug = slug,
            MoleculeId = ParentMoleculeId,
            IsHeadquarters = false
        };

        _db.Companies.Add(company);
        await _db.SaveChangesAsync();
        _companyCacheService.InvalidateCache(company.Id);
        _logger.LogInformation("Company '{Name}' added to molecule {MoleculeId} from hierarchy page", company.Name, ParentMoleculeId);

        TempData["SuccessMessage"] = string.Format(_localizer["Success_CompanyCreated"].Value, company.Name);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRenameCompanyAsync()
    {
        if (EntityId <= 0 || string.IsNullOrWhiteSpace(EntityName))
        {
            TempData["ErrorMessage"] = _localizer["Error_RequiredFields"].Value;
            return RedirectToPage();
        }

        var company = await _db.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == EntityId);
        if (company == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_CompanyNotFound"].Value;
            return RedirectToPage();
        }

        if (company.MoleculeId.HasValue && !await CurrentUserCanManageMoleculeAsync(company.MoleculeId.Value))
            return Forbid();
        if (!company.MoleculeId.HasValue && !await CurrentUserHasManageHierarchyAsync())
            return Forbid();

        if (ContainsDangerousContent(EntityName) || ContainsDangerousContent(EntityDisplayName))
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidInput"].Value;
            return RedirectToPage();
        }

        company.Name = EntityName.Trim();
        company.DisplayName = string.IsNullOrWhiteSpace(EntityDisplayName) ? null : EntityDisplayName.Trim();
        await _db.SaveChangesAsync();
        _companyCacheService.InvalidateCache(EntityId);
        _logger.LogInformation("Company {CompanyId} renamed to '{Name}' from hierarchy page", EntityId, company.Name);

        TempData["SuccessMessage"] = string.Format(_localizer["Success_CompanyRenamed"].Value, company.Name);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteCompanyAsync()
    {
        if (EntityId <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidId"].Value;
            return RedirectToPage();
        }

        var company = await _db.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == EntityId);
        if (company == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_CompanyNotFound"].Value;
            return RedirectToPage();
        }

        if (company.MoleculeId.HasValue && !await CurrentUserCanManageMoleculeAsync(company.MoleculeId.Value))
            return Forbid();
        if (!company.MoleculeId.HasValue && !await CurrentUserHasManageHierarchyAsync())
            return Forbid();

        // Prevent deletion if company has active users
        var activeUserCount = await _db.Users.IgnoreQueryFilters().CountAsync(u => u.CompanyId == EntityId && u.IsActive);
        if (activeUserCount > 0)
        {
            TempData["ErrorMessage"] = string.Format(_localizer["Error_CannotDeleteCompanyWithUsers"].Value, activeUserCount);
            return RedirectToPage();
        }

        using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            // Cascade delete related data
            await _db.SwapRequests.IgnoreQueryFilters().Where(sr => sr.CompanyId == EntityId).ExecuteDeleteAsync();
            await _db.TimeOffRequests.IgnoreQueryFilters().Where(tor => tor.CompanyId == EntityId).ExecuteDeleteAsync();
            await _db.ShiftAssignments.IgnoreQueryFilters().Where(sa => sa.CompanyId == EntityId).ExecuteDeleteAsync();
            await _db.ShiftInstances.IgnoreQueryFilters().Where(si => si.CompanyId == EntityId).ExecuteDeleteAsync();
            await _db.ShiftTypes.Where(st => st.CompanyId == EntityId).ExecuteDeleteAsync(); // Only deletes company-scoped shifts (molecule-scoped have CompanyId=null)
            await _db.Configs.IgnoreQueryFilters().Where(c => c.CompanyId == EntityId).ExecuteDeleteAsync();
            await _db.DirectorCompanies.Where(dc => dc.CompanyId == EntityId).ExecuteDeleteAsync();
            await _db.UserNotifications.IgnoreQueryFilters().Where(n => n.CompanyId == EntityId).ExecuteDeleteAsync();
            await _db.UserJoinRequests.IgnoreQueryFilters().Where(jr => jr.CompanyId == EntityId).ExecuteDeleteAsync();

            // Deactivate remaining users
            await _db.Users.IgnoreQueryFilters().Where(u => u.CompanyId == EntityId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));

            _db.Companies.Remove(company);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete company {CompanyId}", EntityId);
            TempData["ErrorMessage"] = _localizer["Error_AnErrorOccurred"].Value;
            return RedirectToPage();
        }

        _companyCacheService.InvalidateCache(EntityId);
        _logger.LogInformation("Company {CompanyId} '{Name}' deleted from hierarchy page", EntityId, company.Name);

        TempData["SuccessMessage"] = string.Format(_localizer["Success_CompanyDeleted"].Value, company.Name);
        return RedirectToPage();
    }

    // ================================================================
    // Department CRUD handlers
    // ================================================================

    public async Task<IActionResult> OnPostAddDepartmentAsync()
    {
        if (ParentMoleculeId <= 0 || !await CurrentUserCanManageMoleculeAsync(ParentMoleculeId))
            return Forbid();

        if (string.IsNullOrWhiteSpace(EntityName) || ParentMoleculeId <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_RequiredFields"].Value;
            return RedirectToPage();
        }

        if (ContainsDangerousContent(EntityName) || ContainsDangerousContent(EntityDisplayName))
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidInput"].Value;
            return RedirectToPage();
        }

        // Departments should only be added to Tech molecules
        var molecule = await _db.Molecules.FirstOrDefaultAsync(m => m.Id == ParentMoleculeId);
        if (molecule == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_MoleculeNotFound"].Value;
            return RedirectToPage();
        }
        if (molecule.Type != MoleculeType.Tech)
        {
            TempData["ErrorMessage"] = _localizer["Error_CannotAddDepartmentToNonTechMolecule"].Value;
            return RedirectToPage();
        }

        var department = new Department
        {
            MoleculeId = ParentMoleculeId,
            Name = EntityName.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(EntityDisplayName) ? EntityName.Trim() : EntityDisplayName.Trim(),
            IsActive = true
        };

        _db.Departments.Add(department);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Department '{Name}' added to molecule {MoleculeId} from hierarchy page", department.Name, ParentMoleculeId);

        TempData["SuccessMessage"] = string.Format(_localizer["Success_DepartmentCreated"].Value, department.Name);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRenameDepartmentAsync()
    {
        if (EntityId <= 0 || string.IsNullOrWhiteSpace(EntityName))
        {
            TempData["ErrorMessage"] = _localizer["Error_RequiredFields"].Value;
            return RedirectToPage();
        }

        var department = await _db.Departments.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Id == EntityId);
        if (department == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_DepartmentNotFound"].Value;
            return RedirectToPage();
        }

        if (!await CurrentUserCanManageMoleculeAsync(department.MoleculeId))
            return Forbid();

        if (ContainsDangerousContent(EntityName) || ContainsDangerousContent(EntityDisplayName))
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidInput"].Value;
            return RedirectToPage();
        }

        department.Name = EntityName.Trim();
        department.DisplayName = string.IsNullOrWhiteSpace(EntityDisplayName) ? EntityName.Trim() : EntityDisplayName.Trim();
        await _db.SaveChangesAsync();
        _logger.LogInformation("Department {DepartmentId} renamed to '{Name}' from hierarchy page", EntityId, department.Name);

        TempData["SuccessMessage"] = string.Format(_localizer["Success_DepartmentRenamed"].Value, department.Name);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteDepartmentAsync()
    {
        if (EntityId <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidId"].Value;
            return RedirectToPage();
        }

        var department = await _db.Departments
            .IgnoreQueryFilters()
            .Include(d => d.Users)
            .FirstOrDefaultAsync(d => d.Id == EntityId);

        if (department == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_DepartmentNotFound"].Value;
            return RedirectToPage();
        }

        if (!await CurrentUserCanManageMoleculeAsync(department.MoleculeId))
            return Forbid();

        if (department.Users.Any(u => u.IsActive))
        {
            TempData["ErrorMessage"] = _localizer["Error_CannotDeleteDepartmentWithUsers"].Value;
            return RedirectToPage();
        }

        _db.Departments.Remove(department);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Department {DepartmentId} '{Name}' deleted from hierarchy page", EntityId, department.Name);

        TempData["SuccessMessage"] = string.Format(_localizer["Success_DepartmentDeleted"].Value, department.Name);
        return RedirectToPage();
    }

    // ================================================================
    // Helpers
    // ================================================================

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

    private static string GenerateSlug(string name)
    {
        var slug = name.ToLowerInvariant().Trim();
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = Regex.Replace(slug, @"[\s]+", "-");
        slug = Regex.Replace(slug, @"-+", "-");
        slug = slug.Trim('-');
        // For non-Latin names (e.g. Hebrew), the slug may be empty after stripping.
        // Append a unique suffix to avoid duplicate slugs.
        if (string.IsNullOrEmpty(slug))
            slug = "company-" + Guid.NewGuid().ToString("N")[..8];
        return slug;
    }

    private static bool ContainsDangerousContent(string? input)
        => InputSanitizer.ContainsDangerousContent(input);
}
