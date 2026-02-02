using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;

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
    private readonly IConfiguration _configuration;
    private readonly ILogger<GetSignupOptionsModel> _logger;

    public GetSignupOptionsModel(
        AppDbContext db,
        IConfiguration configuration,
        ILogger<GetSignupOptionsModel> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Gets all active molecules for the initial dropdown.
    /// </summary>
    public async Task<IActionResult> OnGetMoleculesAsync()
    {
        if (!IsPublicSignupEnabled())
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
                    AreaId = m.AreaId
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
        if (!IsPublicSignupEnabled())
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
                .Where(c => c.MoleculeId == moleculeId)
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
    /// Gets job types filtered by molecule's area.
    /// </summary>
    public async Task<IActionResult> OnGetJobTypesAsync(int moleculeId)
    {
        if (!IsPublicSignupEnabled())
        {
            return new JsonResult(new { error = "Public signup is disabled" }) { StatusCode = 403 };
        }

        if (moleculeId <= 0)
        {
            return new JsonResult(new { error = "Invalid molecule ID" }) { StatusCode = 400 };
        }

        try
        {
            // Get the area ID from the molecule
            var molecule = await _db.Molecules.FirstOrDefaultAsync(m => m.Id == moleculeId);
            if (molecule == null)
            {
                return new JsonResult(new { error = "Molecule not found" }) { StatusCode = 404 };
            }

            var jobTypes = await _db.JobTypes
                .Where(jt => jt.AreaId == molecule.AreaId && jt.IsActive)
                .OrderBy(jt => jt.SortOrder)
                .ThenBy(jt => jt.DisplayName ?? jt.Name)
                .Select(jt => new JobTypeOption
                {
                    Id = jt.Id,
                    Name = jt.DisplayName ?? jt.Name,
                    Color = jt.Color
                })
                .ToListAsync();

            return new JsonResult(new { jobTypes });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching job types for molecule {MoleculeId}", moleculeId);
            return StatusCode(500, new { error = "Error fetching job types" });
        }
    }

    private bool IsPublicSignupEnabled()
    {
        return _configuration.GetValue<bool>("Features:AllowPublicSignup", false);
    }
}

#region DTOs

public class MoleculeOption
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int AreaId { get; set; }
}

public class CompanyOption
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class JobTypeOption
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
}

#endregion
