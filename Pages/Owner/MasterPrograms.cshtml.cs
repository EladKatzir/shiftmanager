using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using ShiftManager.Models;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.Owner;

/// <summary>
/// Master Programs - Manage collections of Programs (complete weekly schedules).
/// A MasterProgram is a convenience wrapper for applying multiple Programs at once.
/// Migrated to OperationResult-based service calls; structured errors flow through
/// the query-string success/error pattern that the existing view consumes.
/// </summary>
[Authorize(Policy = "Grant:ManagerHomeAccess")]
public class MasterProgramsModel : PageModel
{
    private readonly IMasterProgramService _masterProgramService;
    private readonly IShiftProgramService _programService;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<MasterProgramsModel> _logger;
    private readonly ITenantResolver _tenantResolver;
    private readonly IStringLocalizer<SharedResources> _localizer;

    public MasterProgramsModel(
        IMasterProgramService masterProgramService,
        IShiftProgramService programService,
        IAuditLogService auditLogService,
        ILogger<MasterProgramsModel> logger,
        ITenantResolver tenantResolver,
        IStringLocalizer<SharedResources> localizer)
    {
        _masterProgramService = masterProgramService;
        _programService = programService;
        _auditLogService = auditLogService;
        _logger = logger;
        _tenantResolver = tenantResolver;
        _localizer = localizer;
    }

    public List<MasterProgram> MasterPrograms { get; set; } = new();
    public List<ShiftProgram> AvailablePrograms { get; set; } = new();
    public string? Success { get; set; }
    public string? Error { get; set; }

    [BindProperty] public int? EditMasterProgramId { get; set; }
    [BindProperty] public string MasterProgramName { get; set; } = string.Empty;
    [BindProperty] public string? MasterProgramDescription { get; set; }
    [BindProperty] public List<int> SelectedProgramIds { get; set; } = new();

    [BindProperty] public int GenerateMasterProgramId { get; set; }
    [BindProperty] public DateOnly GenerateStartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    [BindProperty] public DateOnly GenerateEndDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(30));
    [BindProperty] public bool OverwriteExisting { get; set; } = false;

    public async Task OnGetAsync(string? success = null, string? error = null)
    {
        Success = success;
        Error = error;

        var companyId = _tenantResolver.GetCurrentTenantId();

        MasterPrograms = await _masterProgramService.GetCompanyMasterProgramsAsync(companyId, includeInactive: false);
        AvailablePrograms = await _programService.GetCompanyProgramsAsync(companyId, includeInactive: false);
    }

    public async Task<IActionResult> OnPostCreateMasterProgramAsync()
    {
        var companyId = _tenantResolver.GetCurrentTenantId();
        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : 0;

        var result = await _masterProgramService.CreateMasterProgramAsync(
            companyId,
            MasterProgramName,
            MasterProgramDescription,
            SelectedProgramIds,
            userId);

        if (!result.Success)
        {
            return RedirectToPage(new { error = result.ErrorMessage });
        }

        _logger.LogInformation(
            "Created MasterProgram {MasterProgramId} '{Name}' for Company {CompanyId}",
            result.Value!.Id, MasterProgramName, companyId);

        return RedirectToPage(new { success = $"Master program '{MasterProgramName}' created successfully" });
    }

    public async Task<IActionResult> OnPostUpdateMasterProgramAsync()
    {
        if (!EditMasterProgramId.HasValue)
        {
            return RedirectToPage(new { error = "Master program ID is required" });
        }

        var companyId = _tenantResolver.GetCurrentTenantId();
        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : 0;

        var result = await _masterProgramService.UpdateMasterProgramAsync(
            EditMasterProgramId.Value,
            MasterProgramName,
            MasterProgramDescription,
            SelectedProgramIds,
            userId);

        if (!result.Success)
        {
            return RedirectToPage(new { error = result.ErrorMessage });
        }

        _logger.LogInformation(
            "Updated MasterProgram {MasterProgramId} for Company {CompanyId}",
            EditMasterProgramId.Value, companyId);

        return RedirectToPage(new { success = $"Master program '{MasterProgramName}' updated successfully" });
    }

    public async Task<IActionResult> OnPostDeleteMasterProgramAsync(int masterProgramId)
    {
        var companyId = _tenantResolver.GetCurrentTenantId();
        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : 0;

        var masterProgram = await _masterProgramService.GetMasterProgramAsync(masterProgramId);

        if (masterProgram == null)
        {
            return RedirectToPage(new { error = _localizer["Error_MasterProgramService_NotFound"].Value });
        }

        var result = await _masterProgramService.DeleteMasterProgramAsync(masterProgramId, userId);

        if (!result.Success)
        {
            return RedirectToPage(new { error = result.ErrorMessage });
        }

        _logger.LogInformation(
            "Deleted MasterProgram {MasterProgramId} for Company {CompanyId}",
            masterProgramId, companyId);

        return RedirectToPage(new { success = $"Master program '{masterProgram.Name}' deleted" });
    }

    public async Task<IActionResult> OnPostGenerateFromMasterAsync()
    {
        var companyId = _tenantResolver.GetCurrentTenantId();
        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : 0;

        if (GenerateEndDate < GenerateStartDate)
        {
            return RedirectToPage(new { error = _localizer["Error_MasterProgramService_DateRangeInvalid"].Value });
        }

        var daysDiff = GenerateEndDate.DayNumber - GenerateStartDate.DayNumber;
        if (daysDiff > 365)
        {
            return RedirectToPage(new { error = _localizer["Error_MasterProgramService_DateRangeTooLarge"].Value });
        }

        var result = await _masterProgramService.GenerateFromMasterProgramAsync(
            GenerateMasterProgramId,
            GenerateStartDate,
            GenerateEndDate,
            OverwriteExisting);

        if (!result.Success)
        {
            return RedirectToPage(new { error = result.ErrorMessage });
        }

        var totalCount = result.Value!.Values.Sum(list => list.Count);

        _logger.LogInformation(
            "Generated {Count} total instances from MasterProgram {MasterProgramId} for Company {CompanyId} ({Issues} per-program issues)",
            totalCount, GenerateMasterProgramId, companyId, result.Issues.Count);

        var successMsg = $"Generated {totalCount} shift instances from {result.Value.Count} programs ({GenerateStartDate:yyyy-MM-dd} to {GenerateEndDate:yyyy-MM-dd})";
        if (result.Issues.Count > 0)
        {
            successMsg += $" - {result.Issues.Count} program(s) failed; check the logs for details";
        }

        return RedirectToPage(new { success = successMsg });
    }
}
