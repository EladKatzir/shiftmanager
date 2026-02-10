using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;

namespace ShiftManager.Pages.Admin.Organization.Molecules;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:EditMolecule policy;
// molecule management is inherently cross-company hierarchy data
[Authorize(Policy = "Grant:EditMolecule")]
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

    // View Models
    public record MoleculeVM(int Id, string Name, string DisplayName, MoleculeType Type, string AreaName, string ProjectName, bool IsActive, int CompanyCount, int DepartmentCount);
    public record AreaOption(int Id, string Name, string ProjectName);

    // Data
    public List<MoleculeVM> Molecules { get; set; } = new();
    public List<AreaOption> AvailableAreas { get; set; } = new();

    // Form Bindings
    [BindProperty] public string MoleculeName { get; set; } = string.Empty;
    [BindProperty] public string MoleculeDisplayName { get; set; } = string.Empty;
    [BindProperty] public int SelectedAreaId { get; set; }
    [BindProperty] public MoleculeType SelectedType { get; set; }

    public async Task OnGetAsync()
    {
        if (TempData["SuccessMessage"] is string successMsg)
            Success = successMsg;
        if (TempData["ErrorMessage"] is string errorMsg)
            Error = errorMsg;

        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        Molecules = await _db.Molecules
            .IgnoreQueryFilters()
            .Include(m => m.Area)
            .ThenInclude(a => a.Project)
            .OrderBy(m => m.Area.Project.DisplayName)
            .ThenBy(m => m.Area.DisplayName)
            .ThenBy(m => m.Name)
            .Select(m => new MoleculeVM(
                m.Id,
                m.Name,
                m.DisplayName,
                m.Type,
                m.Area.DisplayName,
                m.Area.Project.DisplayName,
                m.IsActive,
                m.Companies.Count,
                m.Departments.Count(d => d.IsActive)
            ))
            .ToListAsync();

        AvailableAreas = await _db.Areas
            .IgnoreQueryFilters()
            .Where(a => a.IsActive)
            .Include(a => a.Project)
            .Where(a => a.Project.IsActive)
            .OrderBy(a => a.Project.DisplayName)
            .ThenBy(a => a.DisplayName)
            .Select(a => new AreaOption(a.Id, a.DisplayName, a.Project.DisplayName))
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (string.IsNullOrWhiteSpace(MoleculeName))
        {
            TempData["ErrorMessage"] = _localizer["Error_MoleculeNameRequired"];
            return RedirectToPage();
        }

        if (SelectedAreaId <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_AreaRequired"];
            return RedirectToPage();
        }

        var area = await _db.Areas.FindAsync(SelectedAreaId);
        if (area == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_AreaNotFound"];
            return RedirectToPage();
        }

        var molecule = new Molecule
        {
            AreaId = SelectedAreaId,
            Name = MoleculeName.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(MoleculeDisplayName) ? MoleculeName.Trim() : MoleculeDisplayName.Trim(),
            Type = SelectedType,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.Molecules.Add(molecule);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Created molecule {MoleculeId}: {MoleculeName} (Type: {Type}) in Area {AreaId}",
            molecule.Id, molecule.Name, molecule.Type, molecule.AreaId);

        TempData["SuccessMessage"] = string.Format(_localizer["Success_MoleculeCreated"], molecule.DisplayName);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(int id)
    {
        // SECURITY-AUDITED: SAFE — requires Grant:EditMolecule policy; consistent with GET handler
        var molecule = await _db.Molecules.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == id);
        if (molecule == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_MoleculeNotFound"];
            return RedirectToPage();
        }

        molecule.IsActive = !molecule.IsActive;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Molecule {MoleculeId} ({MoleculeName}) active status changed to {IsActive}",
            id, molecule.Name, molecule.IsActive);

        TempData["SuccessMessage"] = molecule.IsActive
            ? string.Format(_localizer["Success_MoleculeActivated"], molecule.DisplayName)
            : string.Format(_localizer["Success_MoleculeDeactivated"], molecule.DisplayName);

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        // SECURITY-AUDITED: SAFE — requires Grant:EditMolecule policy; consistent with GET handler
        var molecule = await _db.Molecules
            .IgnoreQueryFilters()
            .Include(m => m.Companies)
            .Include(m => m.Departments)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (molecule == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_MoleculeNotFound"];
            return RedirectToPage();
        }

        // Check for dependencies
        if (molecule.Companies.Any() || molecule.Departments.Any())
        {
            TempData["ErrorMessage"] = _localizer["Error_CannotDeleteMoleculeWithDependencies"];
            return RedirectToPage();
        }

        _db.Molecules.Remove(molecule);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Deleted molecule {MoleculeId}: {MoleculeName}", id, molecule.Name);

        TempData["SuccessMessage"] = string.Format(_localizer["Success_MoleculeDeleted"], molecule.DisplayName);
        return RedirectToPage();
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
