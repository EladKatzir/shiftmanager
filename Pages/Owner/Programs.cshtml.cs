using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Services;
using System.Security.Claims;
using System.Text.Json;

namespace ShiftManager.Pages.Owner;

/// <summary>
/// Programs - Manage weekly schedule templates (Programs) and generate shift instances.
/// Programs define WHEN a specific shift type runs each week.
/// ✅ P1-2: Expanded access from Owner-only to Manager+Director+Owner
/// </summary>
[Authorize(Policy = "Grant:ManagerHomeAccess")]
public class ProgramsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IShiftProgramService _programService;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<ProgramsModel> _logger;
    private readonly ITenantResolver _tenantResolver;

    public ProgramsModel(
        AppDbContext db,
        IShiftProgramService programService,
        IAuditLogService auditLogService,
        ILogger<ProgramsModel> logger,
        ITenantResolver tenantResolver)
    {
        _db = db;
        _programService = programService;
        _auditLogService = auditLogService;
        _logger = logger;
        _tenantResolver = tenantResolver;
    }

    public List<ShiftProgram> Programs { get; set; } = new();
    public List<ShiftType> ShiftTypes { get; set; } = new();
    public string? Success { get; set; }
    public string? Error { get; set; }

    // Create/Edit Program form
    [BindProperty] public int? EditProgramId { get; set; }
    [BindProperty] public int ShiftTypeId { get; set; }
    [BindProperty] public string ProgramName { get; set; } = string.Empty;
    [BindProperty] public int DefaultStaffing { get; set; } = 1;
    [BindProperty] public List<DayOfWeek> SelectedDays { get; set; } = new();
    [BindProperty] public string PerDayStaffingJson { get; set; } = "{}";

    // Generate instances form
    [BindProperty] public int GenerateProgramId { get; set; }
    [BindProperty] public DateOnly GenerateStartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    [BindProperty] public DateOnly GenerateEndDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(30));
    [BindProperty] public bool OverwriteExisting { get; set; } = false;

    public async Task OnGetAsync(string? success = null, string? error = null)
    {
        Success = success;
        Error = error;

        var companyId = _tenantResolver.GetCurrentTenantId();

        Programs = await _programService.GetCompanyProgramsAsync(companyId, includeInactive: false);

        // Load shift types by molecule (shift types are molecule-scoped now)
        var moleculeId = await _db.Companies.Where(c => c.Id == companyId).Select(c => c.MoleculeId).FirstOrDefaultAsync();
        var allShiftTypes = moleculeId.HasValue
            ? await _db.ShiftTypes
                .Where(st => st.MoleculeId == moleculeId)
                .ToListAsync()
            : new List<ShiftType>();
        ShiftTypes = allShiftTypes.OrderBy(st => st.SortOrder).ToList();
    }

    /// <summary>
    /// Creates a new Program with weekly mask.
    /// </summary>
    public async Task<IActionResult> OnPostCreateProgramAsync()
    {
        try
        {
            var companyId = _tenantResolver.GetCurrentTenantId();
            var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : 0;

            // Validate
            if (string.IsNullOrWhiteSpace(ProgramName))
            {
                return RedirectToPage(new { error = "Program name is required" });
            }

            if (SelectedDays == null || SelectedDays.Count == 0)
            {
                return RedirectToPage(new { error = "At least one day must be selected" });
            }

            if (DefaultStaffing < 1)
            {
                return RedirectToPage(new { error = "Default staffing must be at least 1" });
            }

            // Parse per-day staffing overrides
            var perDayStaffing = new Dictionary<DayOfWeek, int>();
            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, int>>(PerDayStaffingJson ?? "{}");
                if (parsed != null)
                {
                    foreach (var kvp in parsed)
                    {
                        if (Enum.TryParse<DayOfWeek>(kvp.Key, out var day))
                        {
                            perDayStaffing[day] = kvp.Value;
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Ignore invalid JSON, use defaults
            }

            // Create Program
            var createResult = await _programService.CreateProgramAsync(
                companyId,
                ShiftTypeId,
                ProgramName,
                SelectedDays,
                DefaultStaffing,
                perDayStaffing.Count > 0 ? perDayStaffing : null,
                userId);

            if (!createResult.Success)
            {
                return RedirectToPage(new { error = createResult.ErrorMessage });
            }

            var program = createResult.Value!;
            // Audit log

            _logger.LogInformation(
                "Created Program {ProgramId} '{Name}' for Company {CompanyId}",
                program.Id, ProgramName, companyId);

            return RedirectToPage(new { success = $"Program '{ProgramName}' created successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Program");
            return RedirectToPage(new { error = "Failed to create program. Please try again." });
        }
    }

    /// <summary>
    /// Updates an existing Program.
    /// </summary>
    public async Task<IActionResult> OnPostUpdateProgramAsync()
    {
        try
        {
            if (!EditProgramId.HasValue)
            {
                return RedirectToPage(new { error = "Program ID is required" });
            }

            var companyId = _tenantResolver.GetCurrentTenantId();
            var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : 0;

            // Validate
            if (string.IsNullOrWhiteSpace(ProgramName))
            {
                return RedirectToPage(new { error = "Program name is required" });
            }

            if (SelectedDays == null || SelectedDays.Count == 0)
            {
                return RedirectToPage(new { error = "At least one day must be selected" });
            }

            // Parse per-day staffing
            var perDayStaffing = new Dictionary<DayOfWeek, int>();
            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, int>>(PerDayStaffingJson ?? "{}");
                if (parsed != null)
                {
                    foreach (var kvp in parsed)
                    {
                        if (Enum.TryParse<DayOfWeek>(kvp.Key, out var day))
                        {
                            perDayStaffing[day] = kvp.Value;
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Ignore
            }

            // Update Program
            await _programService.UpdateProgramAsync(
                EditProgramId.Value,
                ProgramName,
                SelectedDays,
                DefaultStaffing,
                perDayStaffing.Count > 0 ? perDayStaffing : null,
                userId);

            // Audit log

            _logger.LogInformation(
                "Updated Program {ProgramId} for Company {CompanyId}",
                EditProgramId.Value, companyId);

            return RedirectToPage(new { success = $"Program '{ProgramName}' updated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update Program");
            return RedirectToPage(new { error = "Failed to update program. Please try again." });
        }
    }

    /// <summary>
    /// Deletes a Program (soft delete).
    /// </summary>
    public async Task<IActionResult> OnPostDeleteProgramAsync(int programId)
    {
        try
        {
            var companyId = _tenantResolver.GetCurrentTenantId();
            var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : 0;

            var program = await _programService.GetProgramAsync(programId);

            if (program == null)
            {
                return RedirectToPage(new { error = "Program not found" });
            }

            await _programService.DeleteProgramAsync(programId, userId);

            // Audit log

            _logger.LogInformation(
                "Deleted Program {ProgramId} for Company {CompanyId}",
                programId, companyId);

            return RedirectToPage(new { success = $"Program '{program.Name}' deleted" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete Program");
            return RedirectToPage(new { error = "Failed to delete program. Please try again." });
        }
    }

    /// <summary>
    /// Generates ShiftInstances from a Program.
    /// </summary>
    public async Task<IActionResult> OnPostGenerateInstancesAsync()
    {
        try
        {
            var companyId = _tenantResolver.GetCurrentTenantId();
            var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : 0;

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
            var applyResult = await _programService.ApplyProgramToDateRangeAsync(
                GenerateProgramId,
                GenerateStartDate,
                GenerateEndDate,
                OverwriteExisting);

            if (!applyResult.Success)
            {
                return RedirectToPage(new { error = applyResult.ErrorMessage });
            }

            var count = applyResult.Value;
            // Audit log
            var program = await _programService.GetProgramAsync(GenerateProgramId);

            _logger.LogInformation(
                "Generated {Count} instances from Program {ProgramId} for Company {CompanyId}",
                count, GenerateProgramId, companyId);

            return RedirectToPage(new
            {
                success = $"Generated {count} shift instances from {GenerateStartDate:yyyy-MM-dd} to {GenerateEndDate:yyyy-MM-dd}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate instances");
            return RedirectToPage(new { error = "Failed to generate instances. Please try again." });
        }
    }
}
