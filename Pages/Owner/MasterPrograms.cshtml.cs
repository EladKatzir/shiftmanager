using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Models;
using ShiftManager.Services;

namespace ShiftManager.Pages.Owner;

/// <summary>
/// Master Programs - Manage collections of Programs (complete weekly schedules).
/// A MasterProgram is a convenience wrapper for applying multiple Programs at once.
/// ✅ P1-3: Expanded access from Owner-only to Manager+Director+Owner
/// </summary>
[Authorize(Policy = "IsManagerOrAdmin")]
public class MasterProgramsModel : PageModel
{
    private readonly IMasterProgramService _masterProgramService;
    private readonly IShiftProgramService _programService;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<MasterProgramsModel> _logger;
    private readonly ITenantResolver _tenantResolver;

    public MasterProgramsModel(
        IMasterProgramService masterProgramService,
        IShiftProgramService programService,
        IAuditLogService auditLogService,
        ILogger<MasterProgramsModel> logger,
        ITenantResolver tenantResolver)
    {
        _masterProgramService = masterProgramService;
        _programService = programService;
        _auditLogService = auditLogService;
        _logger = logger;
        _tenantResolver = tenantResolver;
    }

    public List<MasterProgram> MasterPrograms { get; set; } = new();
    public List<ShiftProgram> AvailablePrograms { get; set; } = new();
    public string? Success { get; set; }
    public string? Error { get; set; }

    // Create/Edit MasterProgram form
    [BindProperty] public int? EditMasterProgramId { get; set; }
    [BindProperty] public string MasterProgramName { get; set; } = string.Empty;
    [BindProperty] public string? MasterProgramDescription { get; set; }
    [BindProperty] public List<int> SelectedProgramIds { get; set; } = new();

    // Generate instances form
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

    /// <summary>
    /// Creates a new MasterProgram.
    /// </summary>
    public async Task<IActionResult> OnPostCreateMasterProgramAsync()
    {
        try
        {
            var companyId = _tenantResolver.GetCurrentTenantId();
            var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");

            // Validate
            if (string.IsNullOrWhiteSpace(MasterProgramName))
            {
                return RedirectToPage(new { error = "Master program name is required" });
            }

            if (SelectedProgramIds == null || SelectedProgramIds.Count == 0)
            {
                return RedirectToPage(new { error = "At least one program must be selected" });
            }

            // Create MasterProgram
            var masterProgram = await _masterProgramService.CreateMasterProgramAsync(
                companyId,
                MasterProgramName,
                MasterProgramDescription,
                SelectedProgramIds,
                userId);

            // Audit log

            _logger.LogInformation(
                "Created MasterProgram {MasterProgramId} '{Name}' for Company {CompanyId}",
                masterProgram.Id, MasterProgramName, companyId);

            return RedirectToPage(new { success = $"Master program '{MasterProgramName}' created successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create MasterProgram");
            return RedirectToPage(new { error = "Failed to create master program: " + ex.Message });
        }
    }

    /// <summary>
    /// Updates an existing MasterProgram.
    /// </summary>
    public async Task<IActionResult> OnPostUpdateMasterProgramAsync()
    {
        try
        {
            if (!EditMasterProgramId.HasValue)
            {
                return RedirectToPage(new { error = "Master program ID is required" });
            }

            var companyId = _tenantResolver.GetCurrentTenantId();
            var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");

            // Validate
            if (string.IsNullOrWhiteSpace(MasterProgramName))
            {
                return RedirectToPage(new { error = "Master program name is required" });
            }

            if (SelectedProgramIds == null || SelectedProgramIds.Count == 0)
            {
                return RedirectToPage(new { error = "At least one program must be selected" });
            }

            // Update MasterProgram
            await _masterProgramService.UpdateMasterProgramAsync(
                EditMasterProgramId.Value,
                MasterProgramName,
                MasterProgramDescription,
                SelectedProgramIds,
                userId);

            // Audit log

            _logger.LogInformation(
                "Updated MasterProgram {MasterProgramId} for Company {CompanyId}",
                EditMasterProgramId.Value, companyId);

            return RedirectToPage(new { success = $"Master program '{MasterProgramName}' updated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update MasterProgram");
            return RedirectToPage(new { error = "Failed to update master program: " + ex.Message });
        }
    }

    /// <summary>
    /// Deletes a MasterProgram (soft delete).
    /// </summary>
    public async Task<IActionResult> OnPostDeleteMasterProgramAsync(int masterProgramId)
    {
        try
        {
            var companyId = _tenantResolver.GetCurrentTenantId();
            var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");

            var masterProgram = await _masterProgramService.GetMasterProgramAsync(masterProgramId);

            if (masterProgram == null)
            {
                return RedirectToPage(new { error = "Master program not found" });
            }

            await _masterProgramService.DeleteMasterProgramAsync(masterProgramId, userId);

            // Audit log

            _logger.LogInformation(
                "Deleted MasterProgram {MasterProgramId} for Company {CompanyId}",
                masterProgramId, companyId);

            return RedirectToPage(new { success = $"Master program '{masterProgram.Name}' deleted" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete MasterProgram");
            return RedirectToPage(new { error = "Failed to delete master program: " + ex.Message });
        }
    }

    /// <summary>
    /// Generates ShiftInstances from all Programs in a MasterProgram.
    /// </summary>
    public async Task<IActionResult> OnPostGenerateFromMasterAsync()
    {
        try
        {
            var companyId = _tenantResolver.GetCurrentTenantId();
            var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");

            // Validate date range
            if (GenerateEndDate < GenerateStartDate)
            {
                return RedirectToPage(new { error = "End date must be after start date" });
            }

            var daysDiff = GenerateEndDate.DayNumber - GenerateStartDate.DayNumber;
            if (daysDiff > 365)
            {
                return RedirectToPage(new { error = "Date range cannot exceed 1 year" });
            }

            // Generate instances
            var result = await _masterProgramService.GenerateFromMasterProgramAsync(
                GenerateMasterProgramId,
                GenerateStartDate,
                GenerateEndDate,
                OverwriteExisting);

            var totalCount = result.Values.Sum(list => list.Count);

            // Audit log
            var masterProgram = await _masterProgramService.GetMasterProgramAsync(GenerateMasterProgramId);

            _logger.LogInformation(
                "Generated {Count} total instances from MasterProgram {MasterProgramId} for Company {CompanyId}",
                totalCount, GenerateMasterProgramId, companyId);

            return RedirectToPage(new
            {
                success = $"Generated {totalCount} shift instances from {result.Count} programs ({GenerateStartDate:yyyy-MM-dd} to {GenerateEndDate:yyyy-MM-dd})"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate instances from MasterProgram");
            return RedirectToPage(new { error = "Failed to generate instances: " + ex.Message });
        }
    }
}
