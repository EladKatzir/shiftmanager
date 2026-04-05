using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Helpers;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.Admin.Organization.Molecules;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:EditMolecule policy;
// molecule management is inherently cross-company hierarchy data
[Authorize(Policy = "Grant:EditMolecule")]
public class IndexModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<IndexModel> _logger;
    private readonly ISetupTaskService _setupTaskService;
    private readonly IShiftTypeSeedService _shiftTypeSeedService;
    private readonly IAuditLogService _auditLogService;

    public IndexModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        ILogger<IndexModel> logger,
        ISetupTaskService setupTaskService,
        IShiftTypeSeedService shiftTypeSeedService,
        IAuditLogService auditLogService) : base(localizer)
    {
        _db = db;
        _logger = logger;
        _setupTaskService = setupTaskService;
        _shiftTypeSeedService = shiftTypeSeedService;
        _auditLogService = auditLogService;
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

    [BindProperty] public int EditId { get; set; }
    [BindProperty] public string EditName { get; set; } = string.Empty;
    [BindProperty] public string EditDisplayName { get; set; } = string.Empty;

    public async Task OnGetAsync()
    {

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
            TempData["ErrorMessage"] = _localizer["Error_MoleculeNameRequired"].Value;
            return RedirectToPage();
        }

        if (InputSanitizer.ContainsDangerousContent(MoleculeName) || InputSanitizer.ContainsDangerousContent(MoleculeDisplayName))
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidInput"].Value;
            return RedirectToPage();
        }

        if (SelectedAreaId <= 0)
        {
            TempData["ErrorMessage"] = _localizer["Error_AreaRequired"].Value;
            return RedirectToPage();
        }

        var area = await _db.Areas.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == SelectedAreaId);
        if (area == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_AreaNotFound"].Value;
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

        await using var transaction = await _db.Database.BeginTransactionAsync();

        _db.Molecules.Add(molecule);
        await _db.SaveChangesAsync();

        // Auto-create HQ company for Director assignments
        var hqCompany = new Company
        {
            Name = "HQ",
            DisplayName = _localizer["HQ_AllTeams"].Value,
            Slug = $"hq-{molecule.Name.ToLowerInvariant()}",
            MoleculeId = molecule.Id,
            IsHeadquarters = true
        };
        _db.Companies.Add(hqCompany);
        await _db.SaveChangesAsync();

        await transaction.CommitAsync();

        _logger.LogInformation("Created molecule {MoleculeId}: {MoleculeName} (Type: {Type}) in Area {AreaId} with HQ company {HQCompanyId}",
            molecule.Id, molecule.Name, molecule.Type, molecule.AreaId, hqCompany.Id);

        // Auto-seed standard shift types (HOME, OFFLINE) for the new molecule
        await _shiftTypeSeedService.SeedForMoleculeAsync(molecule.Id);

        // Auto-generate setup tasks for the new molecule (duplicate guard: only if none exist)
        var existingTasks = await _setupTaskService.GetTasksForMoleculeAsync(molecule.Id);
        if (!existingTasks.Any())
        {
            if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var currentUserId))
            {
                _logger.LogWarning("Invalid or missing NameIdentifier claim");
                return RedirectToPage("/Auth/Login");
            }
            await _setupTaskService.GenerateTasksForMoleculeAsync(molecule.Id, currentUserId);
            _logger.LogInformation("Auto-generated setup tasks for new molecule {MoleculeId}", molecule.Id);
        }

        await _auditLogService.LogAsync("MoleculeCreated", "Molecule", molecule.Id,
            $"Created molecule '{molecule.DisplayName}' (Type={molecule.Type}) in area (AreaId={molecule.AreaId})");

        TempData["SuccessMessage"] = string.Format(_localizer["Success_MoleculeCreated"], molecule.DisplayName);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRenameAsync()
    {
        if (EditId <= 0 || string.IsNullOrWhiteSpace(EditName))
        {
            TempData["ErrorMessage"] = _localizer["Error_RequiredFields"].Value;
            return RedirectToPage();
        }

        if (InputSanitizer.ContainsDangerousContent(EditName) || InputSanitizer.ContainsDangerousContent(EditDisplayName))
        {
            TempData["ErrorMessage"] = _localizer["Error_InvalidInput"].Value;
            return RedirectToPage();
        }

        // SECURITY-AUDITED: SAFE — requires Grant:EditMolecule policy; consistent with GET handler
        var molecule = await _db.Molecules.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == EditId);
        if (molecule == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_MoleculeNotFound"].Value;
            return RedirectToPage();
        }

        var oldName = molecule.Name;
        molecule.Name = EditName.Trim();
        molecule.DisplayName = string.IsNullOrWhiteSpace(EditDisplayName) ? EditName.Trim() : EditDisplayName.Trim();
        await _db.SaveChangesAsync();

        _logger.LogInformation("Molecule {MoleculeId} renamed from '{OldName}' to '{NewName}'", EditId, oldName, molecule.Name);

        await _auditLogService.LogAsync("MoleculeRenamed", "Molecule", EditId,
            $"Molecule renamed from '{oldName}' to '{molecule.DisplayName}'");

        TempData["SuccessMessage"] = string.Format(_localizer["Success_MoleculeRenamed"], molecule.DisplayName);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(int id)
    {
        // SECURITY-AUDITED: SAFE — requires Grant:EditMolecule policy; consistent with GET handler
        var molecule = await _db.Molecules.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == id);
        if (molecule == null)
        {
            TempData["ErrorMessage"] = _localizer["Error_MoleculeNotFound"].Value;
            return RedirectToPage();
        }

        molecule.IsActive = !molecule.IsActive;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Molecule {MoleculeId} ({MoleculeName}) active status changed to {IsActive}",
            id, molecule.Name, molecule.IsActive);

        await _auditLogService.LogAsync("MoleculeUpdated", "Molecule", id,
            $"Molecule '{molecule.DisplayName}' {(molecule.IsActive ? "activated" : "deactivated")}");

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
            TempData["ErrorMessage"] = _localizer["Error_MoleculeNotFound"].Value;
            return RedirectToPage();
        }

        // Check for dependencies
        if (molecule.Companies.Any() || molecule.Departments.Any())
        {
            TempData["ErrorMessage"] = _localizer["Error_CannotDeleteMoleculeWithDependencies"].Value;
            return RedirectToPage();
        }

        _db.Molecules.Remove(molecule);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Deleted molecule {MoleculeId}: {MoleculeName}", id, molecule.Name);

        await _auditLogService.LogAsync("MoleculeDeleted", "Molecule", id,
            $"Deleted molecule '{molecule.DisplayName}'");

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
