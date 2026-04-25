using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Results;
using ShiftManager.Models.Support;
using ShiftManager.Resources;

namespace ShiftManager.Services;

/// <summary>
/// Service for managing ShiftPrograms (weekly templates) and generating ShiftInstances.
/// Implements the "Blueprints → Programs → Operations" architecture.
///
/// Migrated to <see cref="OperationResult"/> / <see cref="OperationResult{T}"/> as part of the
/// project-wide error-handling overhaul. All errors return localized messages keyed under
/// <c>Error_ShiftProgramService_*</c>.
/// </summary>
public class ShiftProgramService : IShiftProgramService
{
    private readonly AppDbContext _db;
    private readonly ILogger<ShiftProgramService> _logger;
    private readonly ITenantResolver _tenantResolver;
    private readonly IStringLocalizer<SharedResources> _localizer;

    public ShiftProgramService(
        AppDbContext db,
        ILogger<ShiftProgramService> logger,
        ITenantResolver tenantResolver,
        IStringLocalizer<SharedResources> localizer)
    {
        _db = db;
        _logger = logger;
        _tenantResolver = tenantResolver;
        _localizer = localizer;
    }

    // ==================== CRUD Operations ====================

    public async Task<OperationResult<ShiftProgram>> CreateProgramAsync(
        int companyId,
        int shiftTypeId,
        string name,
        List<DayOfWeek> days,
        int defaultStaffing,
        Dictionary<DayOfWeek, int>? perDayStaffing,
        int userId)
    {
        _logger.LogInformation(
            "Creating Program '{Name}' for ShiftType {ShiftTypeId} in Company {CompanyId}",
            name, shiftTypeId, companyId);

        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationResult<ShiftProgram>.Fail(
                "Error_ShiftProgramService_NameRequired",
                _localizer["Error_ShiftProgramService_NameRequired"].Value);
        }

        // Validate ShiftType exists and is accessible from this company's molecule
        var companyMoleculeId = await _db.Companies
            .Where(c => c.Id == companyId)
            .Select(c => c.MoleculeId)
            .FirstOrDefaultAsync();
        var shiftType = await _db.ShiftTypes
            .FirstOrDefaultAsync(st => st.Id == shiftTypeId && st.MoleculeId == companyMoleculeId);

        if (shiftType == null)
        {
            return OperationResult<ShiftProgram>.Fail(
                "Error_ShiftProgramService_ShiftTypeNotInMolecule",
                _localizer["Error_ShiftProgramService_ShiftTypeNotInMolecule"].Value);
        }

        if (days == null || days.Count == 0)
        {
            return OperationResult<ShiftProgram>.Fail(
                "Error_ShiftProgramService_NoDays",
                _localizer["Error_ShiftProgramService_NoDays"].Value);
        }

        // Create Program
        var program = new ShiftProgram
        {
            CompanyId = companyId,
            ShiftTypeId = shiftTypeId,
            Name = name,
            DefaultStaffingRequired = defaultStaffing,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedBy = userId,
            UpdatedBy = userId
        };

        _db.ShiftPrograms.Add(program);
        await _db.SaveChangesAsync(); // Get Program ID

        // Create ProgramDays
        foreach (var day in days)
        {
            var programDay = new ProgramDay
            {
                ProgramId = program.Id,
                DayOfWeek = day,
                StaffingRequired = perDayStaffing?.ContainsKey(day) == true ? perDayStaffing[day] : null
            };
            _db.ProgramDays.Add(programDay);
        }

        await _db.SaveChangesAsync();

        // Reload with navigation properties
        var reloaded = await GetProgramAsync(program.Id) ?? program;
        return OperationResult<ShiftProgram>.Ok(reloaded);
    }

    public async Task<ShiftProgram?> GetProgramAsync(int programId)
    {
        return await _db.ShiftPrograms
            .Include(p => p.ShiftType)
            .Include(p => p.ProgramDays)
            .FirstOrDefaultAsync(p => p.Id == programId);
    }

    public async Task<List<ShiftProgram>> GetCompanyProgramsAsync(int companyId, bool includeInactive = false)
    {
        var query = _db.ShiftPrograms
            .Include(p => p.ShiftType)
            .Include(p => p.ProgramDays)
            .Where(p => p.CompanyId == companyId);

        if (!includeInactive)
        {
            query = query.Where(p => p.IsActive);
        }

        // Load data first, then sort by SortOrder (which is [NotMapped])
        var programs = await query.ToListAsync();
        return programs
            .OrderBy(p => p.ShiftType.SortOrder)
            .ThenBy(p => p.Name)
            .ToList();
    }

    public async Task<OperationResult> UpdateProgramAsync(
        int programId,
        string name,
        List<DayOfWeek> days,
        int defaultStaffing,
        Dictionary<DayOfWeek, int>? perDayStaffing,
        int userId)
    {
        _logger.LogInformation("Updating Program {ProgramId}", programId);

        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationResult.Fail(
                "Error_ShiftProgramService_NameRequired",
                _localizer["Error_ShiftProgramService_NameRequired"].Value);
        }

        var program = await _db.ShiftPrograms
            .Include(p => p.ProgramDays)
            .FirstOrDefaultAsync(p => p.Id == programId);

        if (program == null)
        {
            return OperationResult.Fail(
                "Error_ShiftProgramService_NotFound",
                _localizer["Error_ShiftProgramService_NotFound"].Value);
        }

        if (days == null || days.Count == 0)
        {
            return OperationResult.Fail(
                "Error_ShiftProgramService_NoDays",
                _localizer["Error_ShiftProgramService_NoDays"].Value);
        }

        // Update Program fields
        program.Name = name;
        program.DefaultStaffingRequired = defaultStaffing;
        program.UpdatedAt = DateTime.UtcNow;
        program.UpdatedBy = userId;

        // Replace ProgramDays (remove old, add new)
        _db.ProgramDays.RemoveRange(program.ProgramDays);

        foreach (var day in days)
        {
            var programDay = new ProgramDay
            {
                ProgramId = program.Id,
                DayOfWeek = day,
                StaffingRequired = perDayStaffing?.ContainsKey(day) == true ? perDayStaffing[day] : null
            };
            _db.ProgramDays.Add(programDay);
        }

        await _db.SaveChangesAsync();
        return OperationResult.Ok();
    }

    public async Task<OperationResult> DeleteProgramAsync(int programId, int userId)
    {
        _logger.LogInformation("Soft-deleting Program {ProgramId} by User {UserId}", programId, userId);

        var program = await _db.ShiftPrograms.FirstOrDefaultAsync(p => p.Id == programId);

        if (program == null)
        {
            return OperationResult.Fail(
                "Error_ShiftProgramService_NotFound",
                _localizer["Error_ShiftProgramService_NotFound"].Value);
        }

        program.IsActive = false;
        program.UpdatedAt = DateTime.UtcNow;
        program.UpdatedBy = userId;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Program {ProgramId} marked as inactive", programId);
        return OperationResult.Ok();
    }

    // ==================== Instance Generation ====================

    public async Task<OperationResult<List<ShiftInstance>>> GenerateInstancesAsync(
        int programId,
        DateOnly startDate,
        DateOnly endDate,
        bool overwriteExisting = false)
    {
        _logger.LogInformation(
            "Generating instances for Program {ProgramId} from {StartDate} to {EndDate} (overwrite={Overwrite})",
            programId, startDate, endDate, overwriteExisting);

        var program = await _db.ShiftPrograms
            .Include(p => p.ShiftType)
            .Include(p => p.ProgramDays)
            .FirstOrDefaultAsync(p => p.Id == programId);

        if (program == null)
        {
            return OperationResult<List<ShiftInstance>>.Fail(
                "Error_ShiftProgramService_NotFound",
                _localizer["Error_ShiftProgramService_NotFound"].Value);
        }

        if (program.ProgramDays.Count == 0)
        {
            _logger.LogWarning("Program {ProgramId} has no ProgramDays configured", programId);
            return OperationResult<List<ShiftInstance>>.Ok(new List<ShiftInstance>());
        }

        var createdInstances = new List<ShiftInstance>();
        var programDaysLookup = program.ProgramDays.ToDictionary(pd => pd.DayOfWeek);

        // Iterate through date range
        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            var dayOfWeek = date.DayOfWeek;

            // Check if this day is in the Program's weekly mask
            if (!programDaysLookup.ContainsKey(dayOfWeek))
            {
                continue; // Skip this day
            }

            var programDay = programDaysLookup[dayOfWeek];

            // Determine staffing for this day (use per-day override if set, otherwise default)
            var staffing = programDay.StaffingRequired ?? program.DefaultStaffingRequired;

            // Check for ShiftCapacityOverride (overrides from calendar UI)
            if (program.JobTypeId.HasValue)
            {
                var capacityOverride = await _db.ShiftCapacityOverrides
                    .Where(o => o.ShiftTypeId == program.ShiftTypeId
                        && o.JobTypeId == program.JobTypeId.Value
                        && o.Date == date)
                    .Select(o => (int?)o.Capacity)
                    .FirstOrDefaultAsync();

                if (capacityOverride.HasValue)
                {
                    staffing = capacityOverride.Value;
                    _logger.LogDebug("Applied ShiftCapacityOverride for ShiftType {ShiftTypeId} on {Date}: {Capacity}",
                        program.ShiftTypeId, date, staffing);
                }
            }

            // Check if instance already exists
            var existingInstance = await _db.ShiftInstances
                .FirstOrDefaultAsync(si =>
                    si.CompanyId == program.CompanyId &&
                    si.WorkDate == date &&
                    si.ShiftTypeId == program.ShiftTypeId);

            if (existingInstance != null)
            {
                if (overwriteExisting)
                {
                    // Update existing instance
                    existingInstance.StaffingRequired = staffing;
                    existingInstance.OriginalProgramId = program.Id;
                    existingInstance.IsDetached = false;
                    existingInstance.OverriddenFields = null;
                    existingInstance.UpdatedAt = DateTime.UtcNow;

                    // Update assignment slots to match new staffing requirement
                    var existingAssignments = await _db.ShiftAssignments
                        .Where(a => a.ShiftInstanceId == existingInstance.Id)
                        .ToListAsync();

                    var currentSlotCount = existingAssignments.Count;

                    if (currentSlotCount < staffing)
                    {
                        // Add more empty slots
                        for (int i = currentSlotCount; i < staffing; i++)
                        {
                            var assignment = new ShiftAssignment
                            {
                                CompanyId = program.CompanyId,
                                ShiftInstanceId = existingInstance.Id,
                                UserId = null,
                                TraineeUserId = null,
                                CreatedAt = DateTime.UtcNow
                            };
                            _db.ShiftAssignments.Add(assignment);
                        }
                    }
                    else if (currentSlotCount > staffing)
                    {
                        // Remove excess unassigned slots (keep assigned ones)
                        var unassignedSlots = existingAssignments
                            .Where(a => !a.UserId.HasValue)
                            .OrderByDescending(a => a.Id)
                            .Take(currentSlotCount - staffing)
                            .ToList();

                        _db.ShiftAssignments.RemoveRange(unassignedSlots);

                        var slotsToRemove = currentSlotCount - staffing;
                        if (unassignedSlots.Count < slotsToRemove)
                        {
                            _logger.LogWarning(
                                "Partial capacity reduction for ShiftInstance {InstanceId} on {Date}: " +
                                "removed {Removed} of {Needed} excess slots ({Assigned} assigned users prevent full reduction to {Target})",
                                existingInstance.Id, date, unassignedSlots.Count, slotsToRemove,
                                currentSlotCount - unassignedSlots.Count, staffing);
                        }
                    }

                    _logger.LogDebug("Updated existing ShiftInstance {InstanceId} for {Date} with {Staffing} slots", existingInstance.Id, date, staffing);
                    createdInstances.Add(existingInstance);
                }
                else
                {
                    // Skip existing
                    _logger.LogDebug("Skipped existing ShiftInstance for {Date}", date);
                }

                continue;
            }

            // Create new ShiftInstance
            var instance = new ShiftInstance
            {
                CompanyId = program.CompanyId,
                ShiftTypeId = program.ShiftTypeId,
                WorkDate = date,
                Name = string.Empty, // Use ShiftType default name (via localization)
                StaffingRequired = staffing,
                OriginalProgramId = program.Id,
                IsDetached = false,
                OverriddenFields = null,
                UpdatedAt = DateTime.UtcNow
            };

            _db.ShiftInstances.Add(instance);
            await _db.SaveChangesAsync(); // Save to get instance ID

            // Create empty assignment slots based on staffing requirement
            for (int i = 0; i < staffing; i++)
            {
                var assignment = new ShiftAssignment
                {
                    CompanyId = program.CompanyId,
                    ShiftInstanceId = instance.Id,
                    UserId = null, // Unassigned slot
                    TraineeUserId = null,
                    CreatedAt = DateTime.UtcNow
                };
                _db.ShiftAssignments.Add(assignment);
            }

            createdInstances.Add(instance);

            _logger.LogDebug("Created ShiftInstance for {Date} with {Staffing} empty assignment slots", date, staffing);
        }

        if (createdInstances.Count > 0)
        {
            await _db.SaveChangesAsync();
            _logger.LogInformation("Generated {Count} ShiftInstances for Program {ProgramId}", createdInstances.Count, programId);
        }
        else
        {
            _logger.LogInformation("No new instances created for Program {ProgramId}", programId);
        }

        return OperationResult<List<ShiftInstance>>.Ok(createdInstances);
    }

    public async Task<OperationResult<int>> ApplyProgramToDateRangeAsync(
        int programId,
        DateOnly startDate,
        DateOnly endDate,
        bool overwriteExisting = false)
    {
        var genResult = await GenerateInstancesAsync(programId, startDate, endDate, overwriteExisting);
        if (!genResult.Success)
        {
            return OperationResult<int>.Fail(
                genResult.ErrorKey ?? "Error_ShiftProgramService_NotFound",
                genResult.ErrorMessage ?? _localizer["Error_ShiftProgramService_NotFound"].Value);
        }
        return OperationResult<int>.Ok(genResult.Value?.Count ?? 0);
    }

    // ==================== Detachment & Reset ====================

    public async Task<OperationResult> DetachInstanceAsync(int instanceId, string overrideType)
    {
        _logger.LogInformation("Detaching ShiftInstance {InstanceId} (override: {Type})", instanceId, overrideType);

        var instance = await _db.ShiftInstances.FirstOrDefaultAsync(si => si.Id == instanceId);

        if (instance == null)
        {
            return OperationResult.Fail(
                "Error_ShiftProgramService_InstanceNotFound",
                _localizer["Error_ShiftProgramService_InstanceNotFound"].Value);
        }

        // Parse existing overrides
        var overrides = ShiftInstanceOverride.Parse(instance.OverriddenFields);

        // Set the specific override flag
        switch (overrideType?.ToLowerInvariant())
        {
            case "staffing":
                overrides.Staffing = true;
                break;
            case "time":
                overrides.Time = true;
                break;
            case "name":
                overrides.Name = true;
                break;
            default:
                return OperationResult.Fail(
                    "Error_ShiftProgramService_InvalidOverrideType",
                    _localizer["Error_ShiftProgramService_InvalidOverrideType"].Value);
        }

        // Update instance
        instance.IsDetached = true;
        instance.OverriddenFields = overrides.ToJson();
        instance.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _logger.LogInformation("ShiftInstance {InstanceId} marked as detached", instanceId);
        return OperationResult.Ok();
    }

    public async Task<OperationResult> ResetInstanceToProgramAsync(int instanceId)
    {
        _logger.LogInformation("Resetting ShiftInstance {InstanceId} to Program defaults", instanceId);

        var instance = await _db.ShiftInstances
            .Include(si => si.OriginalProgram)
                .ThenInclude(p => p!.ProgramDays)
            .FirstOrDefaultAsync(si => si.Id == instanceId);

        if (instance == null)
        {
            return OperationResult.Fail(
                "Error_ShiftProgramService_InstanceNotFound",
                _localizer["Error_ShiftProgramService_InstanceNotFound"].Value);
        }

        if (instance.OriginalProgramId == null || instance.OriginalProgram == null)
        {
            return OperationResult.Fail(
                "Error_ShiftProgramService_InstanceNotFromProgram",
                _localizer["Error_ShiftProgramService_InstanceNotFromProgram"].Value);
        }

        var program = instance.OriginalProgram;
        var dayOfWeek = instance.WorkDate.DayOfWeek;

        // Find the ProgramDay for this day of week (if exists)
        var programDay = program.ProgramDays.FirstOrDefault(pd => pd.DayOfWeek == dayOfWeek);

        if (programDay == null)
        {
            return OperationResult.Fail(
                "Error_ShiftProgramService_ProgramDoesNotRunOnDay",
                _localizer["Error_ShiftProgramService_ProgramDoesNotRunOnDay"].Value);
        }

        // Restore staffing to Program defaults
        var originalStaffing = programDay.StaffingRequired ?? program.DefaultStaffingRequired;

        instance.StaffingRequired = originalStaffing;
        instance.Name = string.Empty; // Clear custom name
        instance.IsDetached = false;
        instance.OverriddenFields = null;
        instance.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "ShiftInstance {InstanceId} reset to Program {ProgramId} defaults (staffing={Staffing})",
            instanceId, program.Id, originalStaffing);
        return OperationResult.Ok();
    }

    public async Task<List<ShiftInstance>> GetInstancesFromProgramAsync(
        int programId,
        DateOnly? startDate = null,
        DateOnly? endDate = null)
    {
        var query = _db.ShiftInstances
            .Include(si => si.ShiftType)
            .Where(si => si.OriginalProgramId == programId);

        if (startDate.HasValue)
        {
            query = query.Where(si => si.WorkDate >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(si => si.WorkDate <= endDate.Value);
        }

        return await query
            .OrderBy(si => si.WorkDate)
            .ToListAsync();
    }
}
