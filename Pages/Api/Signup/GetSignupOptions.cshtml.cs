using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Data.SeedData;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;

namespace ShiftManager.Pages.Api.Signup;

/// <summary>
/// API endpoint for signup form cascading dropdowns.
/// Returns molecules, companies (filtered by molecule), and job types (filtered by area).
/// </summary>
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public class GetSignupOptionsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IFeatureFlagService _featureFlagService;
    private readonly ILogger<GetSignupOptionsModel> _logger;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly IRoleService _roleService;
    private readonly IJobTypeService _jobTypeService;

    public GetSignupOptionsModel(
        AppDbContext db,
        IFeatureFlagService featureFlagService,
        ILogger<GetSignupOptionsModel> logger,
        IStringLocalizer<SharedResources> localizer,
        IRoleService roleService,
        IJobTypeService jobTypeService)
    {
        _db = db;
        _featureFlagService = featureFlagService;
        _logger = logger;
        _localizer = localizer;
        _roleService = roleService;
        _jobTypeService = jobTypeService;
    }

    /// <summary>
    /// Gets all active molecules for the initial dropdown.
    /// </summary>
    public async Task<IActionResult> OnGetMoleculesAsync()
    {
        if (!await IsPublicSignupEnabledAsync())
        {
            return new JsonResult(new { error = "Public signup is disabled" }) { StatusCode = 403 };
        }

        try
        {
            var molecules = await _db.Molecules
                .Where(m => m.IsActive)
                .OrderBy(m => m.DisplayName ?? m.Name)
                .Select(m => new MoleculeOption
                {
                    Id = m.Id,
                    Name = m.DisplayName ?? m.Name,
                    AreaId = m.AreaId,
                    Type = (int)m.Type
                })
                .ToListAsync();

            return new JsonResult(new { molecules });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching molecules for signup");
            return StatusCode(500, new { error = "Error fetching molecules" });
        }
    }

    /// <summary>
    /// Gets companies filtered by molecule ID.
    /// </summary>
    public async Task<IActionResult> OnGetCompaniesAsync(int moleculeId)
    {
        if (!await IsPublicSignupEnabledAsync())
        {
            return new JsonResult(new { error = "Public signup is disabled" }) { StatusCode = 403 };
        }

        if (moleculeId <= 0)
        {
            return new JsonResult(new { error = "Invalid molecule ID" }) { StatusCode = 400 };
        }

        try
        {
            var companies = await _db.Companies
                .Where(c => c.MoleculeId == moleculeId && !c.IsHeadquarters)
                .OrderBy(c => c.DisplayName ?? c.Name)
                .Select(c => new CompanyOption
                {
                    Id = c.Id,
                    Name = c.DisplayName ?? c.Name
                })
                .ToListAsync();

            return new JsonResult(new { companies });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching companies for molecule {MoleculeId}", moleculeId);
            return StatusCode(500, new { error = "Error fetching companies" });
        }
    }

    /// <summary>
    /// Gets job types filtered by molecule (molecule-specific + area-wide).
    /// </summary>
    public async Task<IActionResult> OnGetJobTypesAsync(int moleculeId)
    {
        if (!await IsPublicSignupEnabledAsync())
        {
            return new JsonResult(new { error = "Public signup is disabled" }) { StatusCode = 403 };
        }

        if (moleculeId <= 0)
        {
            return new JsonResult(new { error = "Invalid molecule ID" }) { StatusCode = 400 };
        }

        try
        {
            var jobTypesRaw = await _jobTypeService.GetJobTypesForMoleculeAsync(moleculeId);
            var jobTypes = jobTypesRaw
                .Select(jt => new JobTypeOption
                {
                    Id = jt.Id,
                    Name = jt.DisplayName ?? jt.Name,
                    Color = jt.Color,
                    Key = jt.Name
                })
                .ToList();

            return new JsonResult(new { jobTypes });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching job types for molecule {MoleculeId}", moleculeId);
            return StatusCode(500, new { error = "Error fetching job types" });
        }
    }

    /// <summary>
    /// Gets departments filtered by molecule ID (for Tech molecules).
    /// </summary>
    public async Task<IActionResult> OnGetDepartmentsAsync(int moleculeId)
    {
        if (!await IsPublicSignupEnabledAsync())
        {
            return new JsonResult(new { error = "Public signup is disabled" }) { StatusCode = 403 };
        }

        if (moleculeId <= 0)
        {
            return new JsonResult(new { error = "Invalid molecule ID" }) { StatusCode = 400 };
        }

        try
        {
            var departments = await _db.Departments
                .Where(d => d.MoleculeId == moleculeId && d.IsActive)
                .OrderBy(d => d.DisplayName ?? d.Name)
                .Select(d => new DepartmentOption
                {
                    Id = d.Id,
                    Name = d.DisplayName ?? d.Name
                })
                .ToListAsync();

            return new JsonResult(new { departments });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching departments for molecule {MoleculeId}", moleculeId);
            return StatusCode(500, new { error = "Error fetching departments" });
        }
    }

    /// <summary>
    /// Gets all active job types (not cascaded from molecule — all share the same area).
    /// </summary>
    public async Task<IActionResult> OnGetAllJobTypesAsync()
    {
        if (!await IsPublicSignupEnabledAsync())
        {
            return new JsonResult(new { error = "Public signup is disabled" }) { StatusCode = 403 };
        }

        try
        {
            var allJobTypesRaw = await _jobTypeService.GetAllJobTypesAsync();
            var jobTypes = allJobTypesRaw
                .Select(jt => new JobTypeOption
                {
                    Id = jt.Id,
                    Name = jt.DisplayName ?? jt.Name,
                    Color = jt.Color,
                    Key = jt.Name
                })
                .ToList();

            return new JsonResult(new { jobTypes });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching all job types for signup");
            return StatusCode(500, new { error = "Error fetching job types" });
        }
    }

    /// <summary>
    /// Gets role templates visible in signup dropdown.
    /// </summary>
    public async Task<IActionResult> OnGetRoleTemplatesAsync()
    {
        if (!await IsPublicSignupEnabledAsync())
        {
            return new JsonResult(new { error = "Public signup is disabled" }) { StatusCode = 403 };
        }

        try
        {
            var signupTemplates = await _roleService.GetSignupRoleTemplatesAsync();
            var templates = signupTemplates
                .Select(rt => new RoleTemplateOption
                {
                    Id = rt.Id,
                    Key = rt.Key,
                    DisplayNameEN = rt.DisplayNameEN,
                    DisplayNameHE = rt.DisplayNameHE,
                    NameKey = rt.NameKey,
                    DerivedUserRole = rt.DerivedUserRole.HasValue ? (int)rt.DerivedUserRole.Value : (int?)null,
                    ScopeLevel = (int)rt.ScopeLevel
                })
                .ToList();

            // Resolve NameKey to localized display name for each template
            foreach (var t in templates)
            {
                if (!string.IsNullOrEmpty(t.NameKey))
                    t.DisplayName = _localizer[t.NameKey].Value;
            }

            return new JsonResult(new { roleTemplates = templates });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching role templates for signup");
            return StatusCode(500, new { error = "Error fetching role templates" });
        }
    }

    private async Task<bool> IsPublicSignupEnabledAsync()
    {
        return await _featureFlagService.IsEnabledAsync(FeatureFlagSeed.Flags.AllowPublicSignup);
    }
}

#region DTOs

public class MoleculeOption
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int AreaId { get; set; }
    /// <summary>MoleculeType enum value (0=Workforce, 1=Tech, 2=Helper, 3=System)</summary>
    public int Type { get; set; }
}

public class CompanyOption
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class DepartmentOption
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class JobTypeOption
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public string? Key { get; set; }  // Internal name ("Alhut", "BR") for role label mapping
}

public class RoleTemplateOption
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string? DisplayNameEN { get; set; }
    public string? DisplayNameHE { get; set; }
    public string? NameKey { get; set; }
    /// <summary>Localized display name resolved from NameKey (server-side).</summary>
    public string? DisplayName { get; set; }
    public int? DerivedUserRole { get; set; }
    /// <summary>RoleScopeLevel enum value — used by client to filter templates by molecule type.</summary>
    public int ScopeLevel { get; set; }
}

#endregion
