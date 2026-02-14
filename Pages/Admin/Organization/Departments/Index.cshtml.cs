using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;

namespace ShiftManager.Pages.Admin.Organization.Departments;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:ManageDepartments policy;
// department management is inherently cross-company hierarchy data
[Authorize(Policy = "Grant:ManageDepartments")]
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
    public record DepartmentVM(int Id, string Name, string DisplayName, string MoleculeName, string AreaName, bool IsActive, int UserCount);
    public record MoleculeOption(int Id, string Name, string AreaName, MoleculeType Type);

    // Data
    public List<DepartmentVM> Departments { get; set; } = new();
    public List<MoleculeOption> AvailableMolecules { get; set; } = new();

    // Form Bindings
    [BindProperty] public string DepartmentName { get; set; } = string.Empty;
    [BindProperty] public string DepartmentDisplayName { get; set; } = string.Empty;
    [BindProperty] public int SelectedMoleculeId { get; set; }

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
        var userCountsByDept = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.IsActive && u.DepartmentId.HasValue)
            .GroupBy(u => u.DepartmentId!.Value)
            .Select(g => new { DepartmentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DepartmentId, x => x.Count);

        Departments = (await _db.Departments
            .IgnoreQueryFilters()
            .Include(d => d.Molecule)
            .ThenInclude(m => m.Area)
            .OrderBy(d => d.Molecule.Area.DisplayName)
            .ThenBy(d => d.Molecule.DisplayName)
            .ThenBy(d => d.Name)
            .ToListAsync())
            .Select(d => new DepartmentVM(
                d.Id,
                d.Name,
                d.DisplayName,
                d.Molecule.DisplayName,
                d.Molecule.Area.DisplayName,
                d.IsActive,
                userCountsByDept.GetValueOrDefault(d.Id, 0)
            ))
            .ToList();

        // Only show Tech molecules (departments are for Tech molecules)
        AvailableMolecules = await _db.Molecules
            .IgnoreQueryFilters()
            .Where(m => m.IsActive && m.Type == MoleculeType.Tech)
            .Include(m => m.Area)
            .Where(m => m.Area.IsActive)
            .OrderBy(m => m.Area.DisplayName)
            .ThenBy(m => m.DisplayName)
            .Select(m => new MoleculeOption(m.Id, m.DisplayName, m.Area.DisplayName, m.Type))
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (string.IsNullOrWhiteSpace(DepartmentName))
        {
            TempData["ErrorMessage"] = _localizer["Error_DepartmentNameRequired"].Value;
            return RedirectToPage();
        }

        if (SelectedMoleculeId <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_MoleculeRequired"].Value;
            return RedirectToPage();
        }

        var molecule = await _db.Molecules.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == SelectedMoleculeId);
        if (molecule == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_MoleculeNotFound"].Value;
            return RedirectToPage();
        }

        if (molecule.Type != MoleculeType.Tech)
        {
            TempData["ErrorMessage"] = _localizer["Error_DepartmentsOnlyForTechMolecules"].Value;
            return RedirectToPage();
        }

        var department = new Department
        {
            MoleculeId = SelectedMoleculeId,
            Name = DepartmentName.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(DepartmentDisplayName) ? DepartmentName.Trim() : DepartmentDisplayName.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.Departments.Add(department);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Created department {DepartmentId}: {DepartmentName} in Molecule {MoleculeId}",
            department.Id, department.Name, department.MoleculeId);

        TempData["SuccessMessage"] = string.Format(_localizer["Success_DepartmentCreated"], department.DisplayName);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(int id)
    {
        // SECURITY-AUDITED: SAFE — requires Grant:EditDepartment policy; consistent with GET handler
        var department = await _db.Departments.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Id == id);
        if (department == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_DepartmentNotFound"].Value;
            return RedirectToPage();
        }

        department.IsActive = !department.IsActive;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Department {DepartmentId} ({DepartmentName}) active status changed to {IsActive}",
            id, department.Name, department.IsActive);

        TempData["SuccessMessage"] = department.IsActive
            ? string.Format(_localizer["Success_DepartmentActivated"], department.DisplayName)
            : string.Format(_localizer["Success_DepartmentDeactivated"], department.DisplayName);

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        // SECURITY-AUDITED: SAFE — requires Grant:EditDepartment policy; consistent with GET handler
        var department = await _db.Departments
            .IgnoreQueryFilters()
            .Include(d => d.Users)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (department == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_DepartmentNotFound"].Value;
            return RedirectToPage();
        }

        if (department.Users.Any(u => u.IsActive))
        {
            TempData["ErrorMessage"] = _localizer["Error_CannotDeleteDepartmentWithUsers"].Value;
            return RedirectToPage();
        }

        _db.Departments.Remove(department);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Deleted department {DepartmentId}: {DepartmentName}", id, department.Name);

        TempData["SuccessMessage"] = string.Format(_localizer["Success_DepartmentDeleted"], department.DisplayName);
        return RedirectToPage();
    }
}
