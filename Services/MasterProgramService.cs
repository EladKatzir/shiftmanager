using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Results;
using ShiftManager.Resources;

namespace ShiftManager.Services;

/// <summary>
/// Service for managing MasterPrograms (collections of Programs).
/// Orchestrates ShiftProgramService to generate shifts from multiple Programs at once.
///
/// Migrated to <see cref="OperationResult"/> / <see cref="OperationResult{T}"/> as part of the
/// project-wide error-handling overhaul. All errors return localized messages via
/// <see cref="IStringLocalizer{SharedResources}"/> with keys <c>Error_MasterProgramService_*</c>.
/// </summary>
public class MasterProgramService : IMasterProgramService
{
    private readonly AppDbContext _db;
    private readonly ILogger<MasterProgramService> _logger;
    private readonly ITenantResolver _tenantResolver;
    private readonly IShiftProgramService _shiftProgramService;
    private readonly ICompanyLocalizationService _localizationService;
    private readonly IStringLocalizer<SharedResources> _localizer;

    public MasterProgramService(
        AppDbContext db,
        ILogger<MasterProgramService> logger,
        ITenantResolver tenantResolver,
        IShiftProgramService shiftProgramService,
        ICompanyLocalizationService localizationService,
        IStringLocalizer<SharedResources> localizer)
    {
        _db = db;
        _logger = logger;
        _tenantResolver = tenantResolver;
        _shiftProgramService = shiftProgramService;
        _localizationService = localizationService;
        _localizer = localizer;
    }

    // ==================== CRUD Operations ====================

    public async Task<OperationResult<MasterProgram>> CreateMasterProgramAsync(
        int companyId,
        string name,
        string? description,
        List<int> programIds,
        int userId)
    {
        _logger.LogInformation(
            "Creating MasterProgram '{Name}' with {Count} Programs in Company {CompanyId}",
            name, programIds?.Count ?? 0, companyId);

        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationResult<MasterProgram>.Fail(
                "Error_MasterProgramService_NameRequired",
                _localizer["Error_MasterProgramService_NameRequired"].Value);
        }

        if (programIds == null || programIds.Count == 0)
        {
            return OperationResult<MasterProgram>.Fail(
                "Error_MasterProgramService_NoPrograms",
                _localizer["Error_MasterProgramService_NoPrograms"].Value);
        }

        var programs = await _db.ShiftPrograms
            .Where(p => programIds.Contains(p.Id) && p.CompanyId == companyId)
            .ToListAsync();

        if (programs.Count != programIds.Count)
        {
            return OperationResult<MasterProgram>.Fail(
                "Error_MasterProgramService_ProgramsNotInCompany",
                _localizer["Error_MasterProgramService_ProgramsNotInCompany"].Value);
        }

        var masterProgram = new MasterProgram
        {
            CompanyId = companyId,
            Name = name,
            Description = description,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedBy = userId,
            UpdatedBy = userId
        };

        _db.MasterPrograms.Add(masterProgram);
        await _db.SaveChangesAsync();

        for (int i = 0; i < programIds.Count; i++)
        {
            var item = new MasterProgramItem
            {
                MasterProgramId = masterProgram.Id,
                ProgramId = programIds[i],
                SortOrder = i
            };
            _db.MasterProgramItems.Add(item);
        }

        await _db.SaveChangesAsync();

        var reloaded = await GetMasterProgramAsync(masterProgram.Id) ?? masterProgram;
        return OperationResult<MasterProgram>.Ok(reloaded);
    }

    public async Task<MasterProgram?> GetMasterProgramAsync(int masterProgramId)
    {
        return await _db.MasterPrograms
            .Include(mp => mp.Items)
                .ThenInclude(item => item.Program)
                    .ThenInclude(p => p.ShiftType)
            .Include(mp => mp.Items)
                .ThenInclude(item => item.Program)
                    .ThenInclude(p => p.ProgramDays)
            .FirstOrDefaultAsync(mp => mp.Id == masterProgramId);
    }

    public async Task<List<MasterProgram>> GetCompanyMasterProgramsAsync(int companyId, bool includeInactive = false)
    {
        var query = _db.MasterPrograms
            .Include(mp => mp.Items)
                .ThenInclude(item => item.Program)
                    .ThenInclude(p => p.ShiftType)
            .Where(mp => mp.CompanyId == companyId);

        if (!includeInactive)
        {
            query = query.Where(mp => mp.IsActive);
        }

        return await query
            .OrderBy(mp => mp.Name)
            .ToListAsync();
    }

    public async Task<OperationResult> UpdateMasterProgramAsync(
        int masterProgramId,
        string name,
        string? description,
        List<int> programIds,
        int userId)
    {
        _logger.LogInformation("Updating MasterProgram {MasterProgramId}", masterProgramId);

        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationResult.Fail(
                "Error_MasterProgramService_NameRequired",
                _localizer["Error_MasterProgramService_NameRequired"].Value);
        }

        var masterProgram = await _db.MasterPrograms
            .Include(mp => mp.Items)
            .FirstOrDefaultAsync(mp => mp.Id == masterProgramId);

        if (masterProgram == null)
        {
            return OperationResult.Fail(
                "Error_MasterProgramService_NotFound",
                _localizer["Error_MasterProgramService_NotFound"].Value);
        }

        if (programIds == null || programIds.Count == 0)
        {
            return OperationResult.Fail(
                "Error_MasterProgramService_NoPrograms",
                _localizer["Error_MasterProgramService_NoPrograms"].Value);
        }

        var programs = await _db.ShiftPrograms
            .Where(p => programIds.Contains(p.Id) && p.CompanyId == masterProgram.CompanyId)
            .ToListAsync();

        if (programs.Count != programIds.Count)
        {
            return OperationResult.Fail(
                "Error_MasterProgramService_ProgramsNotInCompany",
                _localizer["Error_MasterProgramService_ProgramsNotInCompany"].Value);
        }

        masterProgram.Name = name;
        masterProgram.Description = description;
        masterProgram.UpdatedAt = DateTime.UtcNow;
        masterProgram.UpdatedBy = userId;

        _db.MasterProgramItems.RemoveRange(masterProgram.Items);

        for (int i = 0; i < programIds.Count; i++)
        {
            var item = new MasterProgramItem
            {
                MasterProgramId = masterProgram.Id,
                ProgramId = programIds[i],
                SortOrder = i
            };
            _db.MasterProgramItems.Add(item);
        }

        await _db.SaveChangesAsync();
        return OperationResult.Ok();
    }

    public async Task<OperationResult> DeleteMasterProgramAsync(int masterProgramId, int userId)
    {
        _logger.LogInformation("Soft-deleting MasterProgram {MasterProgramId} by User {UserId}", masterProgramId, userId);

        var masterProgram = await _db.MasterPrograms.FirstOrDefaultAsync(mp => mp.Id == masterProgramId);

        if (masterProgram == null)
        {
            return OperationResult.Fail(
                "Error_MasterProgramService_NotFound",
                _localizer["Error_MasterProgramService_NotFound"].Value);
        }

        masterProgram.IsActive = false;
        masterProgram.UpdatedAt = DateTime.UtcNow;
        masterProgram.UpdatedBy = userId;

        await _db.SaveChangesAsync();

        _logger.LogInformation("MasterProgram {MasterProgramId} marked as inactive", masterProgramId);
        return OperationResult.Ok();
    }

    // ==================== Instance Generation ====================

    public async Task<OperationResult<Dictionary<int, List<ShiftInstance>>>> GenerateFromMasterProgramAsync(
        int masterProgramId,
        DateOnly startDate,
        DateOnly endDate,
        bool overwriteExisting = false)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (startDate < today)
        {
            _logger.LogWarning(
                "MasterProgram {MasterProgramId}: adjusting start date from {OriginalStart} to {Today} (skipping past dates)",
                masterProgramId, startDate, today);
            startDate = today;
        }

        if (startDate > endDate)
        {
            _logger.LogInformation(
                "MasterProgram {MasterProgramId}: no future dates to generate (start {Start} > end {End})",
                masterProgramId, startDate, endDate);
            return OperationResult<Dictionary<int, List<ShiftInstance>>>.Ok(new Dictionary<int, List<ShiftInstance>>());
        }

        _logger.LogInformation(
            "Generating instances from MasterProgram {MasterProgramId} from {StartDate} to {EndDate}",
            masterProgramId, startDate, endDate);

        var masterProgram = await GetMasterProgramAsync(masterProgramId);

        if (masterProgram == null)
        {
            return OperationResult<Dictionary<int, List<ShiftInstance>>>.Fail(
                "Error_MasterProgramService_NotFound",
                _localizer["Error_MasterProgramService_NotFound"].Value);
        }

        if (masterProgram.Items.Count == 0)
        {
            return OperationResult<Dictionary<int, List<ShiftInstance>>>.Fail(
                "Error_MasterProgramService_NoProgramsInMaster",
                _localizer["Error_MasterProgramService_NoProgramsInMaster"].Value);
        }

        var result = new Dictionary<int, List<ShiftInstance>>();
        var failures = new List<ValidationIssue>();

        foreach (var item in masterProgram.Items.OrderBy(i => i.SortOrder))
        {
            _logger.LogDebug(
                "Generating instances for Program {ProgramId} ({ProgramName})",
                item.ProgramId, item.Program.Name);

            try
            {
                var genResult = await _shiftProgramService.GenerateInstancesAsync(
                    item.ProgramId,
                    startDate,
                    endDate,
                    overwriteExisting);

                if (genResult.Success && genResult.Value != null)
                {
                    result[item.ProgramId] = genResult.Value;
                    _logger.LogDebug(
                        "Generated {Count} instances for Program {ProgramId}",
                        genResult.Value.Count, item.ProgramId);
                }
                else
                {
                    result[item.ProgramId] = new List<ShiftInstance>();
                    failures.Add(new ValidationIssue(
                        genResult.ErrorKey ?? $"Error_MasterProgramService_GenerateFailed_Program_{item.ProgramId}",
                        $"{item.Program.Name}: {genResult.ErrorMessage ?? "Unknown failure"}",
                        ValidationSeverity.Warning,
                        ValidationCategory.Persistence));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unexpected exception generating instances for Program {ProgramId} in MasterProgram {MasterProgramId}",
                    item.ProgramId, masterProgramId);

                result[item.ProgramId] = new List<ShiftInstance>();
                failures.Add(new ValidationIssue(
                    $"Error_MasterProgramService_GenerateFailed_Program_{item.ProgramId}",
                    $"{item.Program.Name}: {ex.Message}",
                    ValidationSeverity.Warning,
                    ValidationCategory.Persistence));
            }
        }

        var totalInstances = result.Values.Sum(list => list.Count);
        _logger.LogInformation(
            "Generated {TotalInstances} total instances from MasterProgram {MasterProgramId} across {ProgramCount} Programs ({FailureCount} program failures)",
            totalInstances, masterProgramId, result.Count, failures.Count);

        return new OperationResult<Dictionary<int, List<ShiftInstance>>>(
            Success: true,
            Value: result,
            ErrorKey: failures.Count > 0 ? "Error_MasterProgramService_GenerateFailed" : null,
            ErrorMessage: failures.Count > 0 ? _localizer["Error_MasterProgramService_GenerateFailed"].Value : null,
            Issues: failures);
    }

    public async Task<OperationResult<MasterProgramSummary>> GetMasterProgramSummaryAsync(int masterProgramId)
    {
        var masterProgram = await GetMasterProgramAsync(masterProgramId);

        if (masterProgram == null)
        {
            return OperationResult<MasterProgramSummary>.Fail(
                "Error_MasterProgramService_NotFound",
                _localizer["Error_MasterProgramService_NotFound"].Value);
        }

        var companyId = _tenantResolver.GetCurrentTenantId();
        var culture = CultureInfo.CurrentUICulture.Name;
        var shiftTypeNameSet = new HashSet<string>();
        foreach (var item in masterProgram.Items)
            shiftTypeNameSet.Add(await _localizationService.ResolveShiftTypeNameAsync(item.Program.ShiftType, companyId, culture));

        var summary = new MasterProgramSummary
        {
            TotalPrograms = masterProgram.Items.Count,
            UniqueShiftTypes = masterProgram.Items
                .Select(i => i.Program.ShiftTypeId)
                .Distinct()
                .Count(),
            TotalProgramDays = masterProgram.Items
                .SelectMany(i => i.Program.ProgramDays)
                .Count(),
            ShiftTypeNames = shiftTypeNameSet.OrderBy(name => name).ToList()
        };

        return OperationResult<MasterProgramSummary>.Ok(summary);
    }
}
