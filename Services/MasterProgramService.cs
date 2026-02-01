using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;

namespace ShiftManager.Services;

/// <summary>
/// Service for managing MasterPrograms (collections of Programs).
/// Orchestrates ShiftProgramService to generate shifts from multiple Programs at once.
/// </summary>
public class MasterProgramService : IMasterProgramService
{
    private readonly AppDbContext _db;
    private readonly ILogger<MasterProgramService> _logger;
    private readonly ITenantResolver _tenantResolver;
    private readonly IShiftProgramService _shiftProgramService;

    public MasterProgramService(
        AppDbContext db,
        ILogger<MasterProgramService> logger,
        ITenantResolver tenantResolver,
        IShiftProgramService shiftProgramService)
    {
        _db = db;
        _logger = logger;
        _tenantResolver = tenantResolver;
        _shiftProgramService = shiftProgramService;
    }

    // ==================== CRUD Operations ====================

    public async Task<MasterProgram> CreateMasterProgramAsync(
        int companyId,
        string name,
        string? description,
        List<int> programIds,
        int userId)
    {
        _logger.LogInformation(
            "Creating MasterProgram '{Name}' with {Count} Programs in Company {CompanyId}",
            name, programIds.Count, companyId);

        // Validate at least one Program
        if (programIds == null || programIds.Count == 0)
        {
            throw new ArgumentException("At least one Program must be included", nameof(programIds));
        }

        // Validate all Programs exist and belong to company
        var programs = await _db.ShiftPrograms
            .Where(p => programIds.Contains(p.Id) && p.CompanyId == companyId)
            .ToListAsync();

        if (programs.Count != programIds.Count)
        {
            throw new InvalidOperationException(
                $"One or more Programs not found or do not belong to Company {companyId}");
        }

        // Create MasterProgram
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
        await _db.SaveChangesAsync(); // Get ID

        // Create MasterProgramItems (with sort order based on input order)
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

        // Reload with navigation properties
        return await GetMasterProgramAsync(masterProgram.Id) ?? masterProgram;
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

    public async Task UpdateMasterProgramAsync(
        int masterProgramId,
        string name,
        string? description,
        List<int> programIds,
        int userId)
    {
        _logger.LogInformation("Updating MasterProgram {MasterProgramId}", masterProgramId);

        var masterProgram = await _db.MasterPrograms
            .Include(mp => mp.Items)
            .FirstOrDefaultAsync(mp => mp.Id == masterProgramId);

        if (masterProgram == null)
        {
            throw new InvalidOperationException($"MasterProgram {masterProgramId} not found");
        }

        // Validate at least one Program
        if (programIds == null || programIds.Count == 0)
        {
            throw new ArgumentException("At least one Program must be included", nameof(programIds));
        }

        // Validate all Programs exist and belong to same company
        var programs = await _db.ShiftPrograms
            .Where(p => programIds.Contains(p.Id) && p.CompanyId == masterProgram.CompanyId)
            .ToListAsync();

        if (programs.Count != programIds.Count)
        {
            throw new InvalidOperationException(
                $"One or more Programs not found or do not belong to Company {masterProgram.CompanyId}");
        }

        // Update fields
        masterProgram.Name = name;
        masterProgram.Description = description;
        masterProgram.UpdatedAt = DateTime.UtcNow;
        masterProgram.UpdatedBy = userId;

        // Replace Items (remove old, add new)
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
    }

    public async Task DeleteMasterProgramAsync(int masterProgramId, int userId)
    {
        _logger.LogInformation("Soft-deleting MasterProgram {MasterProgramId} by User {UserId}", masterProgramId, userId);

        var masterProgram = await _db.MasterPrograms.FirstOrDefaultAsync(mp => mp.Id == masterProgramId);

        if (masterProgram == null)
        {
            throw new InvalidOperationException($"MasterProgram {masterProgramId} not found");
        }

        masterProgram.IsActive = false;
        masterProgram.UpdatedAt = DateTime.UtcNow;
        masterProgram.UpdatedBy = userId;

        await _db.SaveChangesAsync();

        _logger.LogInformation("MasterProgram {MasterProgramId} marked as inactive", masterProgramId);
    }

    // ==================== Instance Generation ====================

    public async Task<Dictionary<int, List<ShiftInstance>>> GenerateFromMasterProgramAsync(
        int masterProgramId,
        DateOnly startDate,
        DateOnly endDate,
        bool overwriteExisting = false)
    {
        _logger.LogInformation(
            "Generating instances from MasterProgram {MasterProgramId} from {StartDate} to {EndDate}",
            masterProgramId, startDate, endDate);

        var masterProgram = await GetMasterProgramAsync(masterProgramId);

        if (masterProgram == null)
        {
            throw new InvalidOperationException($"MasterProgram {masterProgramId} not found");
        }

        if (masterProgram.Items.Count == 0)
        {
            _logger.LogWarning("MasterProgram {MasterProgramId} has no Programs", masterProgramId);
            return new Dictionary<int, List<ShiftInstance>>();
        }

        var result = new Dictionary<int, List<ShiftInstance>>();

        // Generate instances for each Program in the MasterProgram
        foreach (var item in masterProgram.Items.OrderBy(i => i.SortOrder))
        {
            _logger.LogDebug(
                "Generating instances for Program {ProgramId} ({ProgramName})",
                item.ProgramId, item.Program.Name);

            try
            {
                var instances = await _shiftProgramService.GenerateInstancesAsync(
                    item.ProgramId,
                    startDate,
                    endDate,
                    overwriteExisting);

                result[item.ProgramId] = instances;

                _logger.LogDebug(
                    "Generated {Count} instances for Program {ProgramId}",
                    instances.Count, item.ProgramId);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to generate instances for Program {ProgramId} in MasterProgram {MasterProgramId}",
                    item.ProgramId, masterProgramId);

                // Add empty list for this Program but continue with others
                result[item.ProgramId] = new List<ShiftInstance>();
            }
        }

        var totalInstances = result.Values.Sum(list => list.Count);
        _logger.LogInformation(
            "Generated {TotalInstances} total instances from MasterProgram {MasterProgramId} across {ProgramCount} Programs",
            totalInstances, masterProgramId, result.Count);

        return result;
    }

    public async Task<MasterProgramSummary> GetMasterProgramSummaryAsync(int masterProgramId)
    {
        var masterProgram = await GetMasterProgramAsync(masterProgramId);

        if (masterProgram == null)
        {
            throw new InvalidOperationException($"MasterProgram {masterProgramId} not found");
        }

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
            ShiftTypeNames = masterProgram.Items
                .Select(i => i.Program.ShiftType.Name)
                .Distinct()
                .OrderBy(name => name)
                .ToList()
        };

        return summary;
    }
}
