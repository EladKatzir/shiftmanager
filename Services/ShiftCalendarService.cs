// Services/ShiftCalendarService.cs
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — shift calendar is molecule-scoped by design;
// queries scoped by explicit moleculeId/jobTypeId parameters; called only from authorized calendar pages
public class ShiftCalendarService : IShiftCalendarService
{
    private readonly AppDbContext _db;
    private readonly ILogger<ShiftCalendarService> _logger;
    private readonly ICompanyCacheService _companyCacheService;
    private readonly ICompanyLocalizationService _localizationService;
    private const int DEFAULT_REST_HOURS = 8;
    private const int DEFAULT_STAFFING_REQUIRED = 1;

    public ShiftCalendarService(AppDbContext db, ILogger<ShiftCalendarService> logger, ICompanyCacheService companyCacheService, ICompanyLocalizationService localizationService)
    {
        _db = db;
        _logger = logger;
        _companyCacheService = companyCacheService;
        _localizationService = localizationService;
    }

    public async Task<List<AppUser>> GetUsersForCalendarAsync(int moleculeId, int? jobTypeId)
    {
        // C-07 OPTIMIZED: Single query with join instead of two separate queries
        // SECURITY-AUDITED: SAFE — re-scoped by molecule membership + jobTypeId
        var query = _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.IsActive
                && u.AccountType != AccountType.GroupUser
                && _db.Companies.Any(c => c.Id == u.CompanyId && c.MoleculeId == moleculeId));

        if (jobTypeId.HasValue)
            query = query.Where(u => u.JobTypeId == jobTypeId.Value);

        return await query
            .Include(u => u.JobType)
            .OrderBy(u => u.DisplayName)
            .ToListAsync();
    }

    public async Task<List<ShiftInstance>> GetShiftInstancesAsync(int moleculeId, int? jobTypeId, DateOnly start, DateOnly end)
    {
        // SECURITY-AUDITED: SAFE — scoped by moleculeId + jobTypeId + date range
        var query = _db.ShiftInstances
            .IgnoreQueryFilters()
            .Include(si => si.ShiftType)
            .Where(si => si.ShiftType.MoleculeId == moleculeId
                && si.WorkDate >= start
                && si.WorkDate <= end);

        if (jobTypeId.HasValue)
            // Include instances for this JobType OR null JobType (shared shifts like Home/Offline)
            query = query.Where(si => si.ShiftType.JobTypeId == jobTypeId.Value || si.ShiftType.JobTypeId == null);
        else
            query = query.Where(si => si.ShiftType.JobTypeId == null);

        return await query.ToListAsync();
    }

    public async Task<List<ShiftAssignment>> GetAssignmentsAsync(int moleculeId, int? jobTypeId, DateOnly start, DateOnly end)
    {
        // SECURITY-AUDITED: SAFE — scoped by moleculeId + jobTypeId + date range
        var query = _db.ShiftAssignments
            .IgnoreQueryFilters()
            .Include(sa => sa.User)
            .Include(sa => sa.Trainee)
            .Include(sa => sa.ShiftInstance)
                .ThenInclude(si => si.ShiftType)
            // HOME unification (Task 22): expose source TimeOffRequest so callers can render
            // vacation/after/rotation source icons on HOME chips. Nullable for non-HOME and
            // rotation-sourced HOME assignments.
            .Include(sa => sa.SourceTimeOffRequest)
            .AsSplitQuery() // C-07: split multi-include query to avoid cartesian explosion
            .Where(sa => sa.ShiftInstance.ShiftType.MoleculeId == moleculeId
                && sa.ShiftInstance.WorkDate >= start
                && sa.ShiftInstance.WorkDate <= end);

        if (jobTypeId.HasValue)
            // Include assignments for this JobType OR null JobType (shared shifts like Home/Offline)
            query = query.Where(sa => sa.ShiftInstance.ShiftType.JobTypeId == jobTypeId.Value || sa.ShiftInstance.ShiftType.JobTypeId == null);
        else
            query = query.Where(sa => sa.ShiftInstance.ShiftType.JobTypeId == null);

        return await query.ToListAsync();
    }

    public async Task<int> GetCapacityAsync(int shiftTypeId, int moleculeId, int? jobTypeId, DateOnly date)
    {
        var query = _db.ShiftCapacityOverrides
            .Where(o => o.ShiftTypeId == shiftTypeId
                && o.MoleculeId == moleculeId
                && o.Date == date);

        if (jobTypeId.HasValue)
            query = query.Where(o => o.JobTypeId == jobTypeId.Value);
        else
            query = query.Where(o => o.JobTypeId == null);

        var overrideCapacity = await query
            .Select(o => (int?)o.Capacity)
            .FirstOrDefaultAsync();

        if (overrideCapacity.HasValue)
            return overrideCapacity.Value;

        return await GetDefaultCapacityAsync(shiftTypeId);
    }

    public async Task<int> GetDefaultCapacityAsync(int shiftTypeId)
    {
        // ShiftType doesn't have DefaultStaffingRequired, so we return a constant default
        // The actual capacity is typically set via ShiftInstance.StaffingRequired or ShiftCapacityOverride
        // SECURITY-AUDITED: SAFE — scoped by specific shiftTypeId
        var shiftType = await _db.ShiftTypes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(st => st.Id == shiftTypeId);
        if (shiftType == null)
            return DEFAULT_STAFFING_REQUIRED;

        // Look for a matching ShiftInstance to get its default staffing
        // SECURITY-AUDITED: SAFE — scoped by specific shiftTypeId
        var instance = await _db.ShiftInstances
            .IgnoreQueryFilters()
            .Where(si => si.ShiftTypeId == shiftTypeId)
            .FirstOrDefaultAsync();

        return instance?.StaffingRequired ?? DEFAULT_STAFFING_REQUIRED;
    }

    public async Task<Dictionary<(int ShiftTypeId, DateOnly Date), int>> GetCapacitiesBatchAsync(
        int moleculeId, int? jobTypeId, DateOnly start, DateOnly end)
    {
        var result = new Dictionary<(int ShiftTypeId, DateOnly Date), int>();

        // 1) Batch-load all capacity overrides for this scope and date range (single query)
        var overrideQuery = _db.ShiftCapacityOverrides
            .Where(o => o.MoleculeId == moleculeId
                && o.Date >= start
                && o.Date <= end);

        if (jobTypeId.HasValue)
            overrideQuery = overrideQuery.Where(o => o.JobTypeId == jobTypeId.Value);
        else
            overrideQuery = overrideQuery.Where(o => o.JobTypeId == null);

        var overrides = await overrideQuery.ToListAsync();
        var overrideLookup = overrides.ToLookup(o => (o.ShiftTypeId, o.Date));

        // 2) Batch-load all shift instances to get default StaffingRequired (single query)
        // SECURITY-AUDITED: SAFE — scoped by moleculeId + jobTypeId + date range
        var instanceQuery = _db.ShiftInstances
            .IgnoreQueryFilters()
            .Include(si => si.ShiftType)
            .Where(si => si.ShiftType.MoleculeId == moleculeId
                && si.WorkDate >= start
                && si.WorkDate <= end);

        if (jobTypeId.HasValue)
            instanceQuery = instanceQuery.Where(si => si.ShiftType.JobTypeId == jobTypeId.Value);
        else
            instanceQuery = instanceQuery.Where(si => si.ShiftType.JobTypeId == null);

        var instances = await instanceQuery.ToListAsync();

        foreach (var instance in instances)
        {
            var key = (instance.ShiftTypeId, instance.WorkDate);
            if (result.ContainsKey(key))
                continue;

            var overrideEntry = overrideLookup[key].FirstOrDefault();
            result[key] = overrideEntry?.Capacity ?? instance.StaffingRequired;
        }

        return result;
    }

    public async Task SetCapacityOverrideAsync(int shiftTypeId, int moleculeId, int? jobTypeId, DateOnly date, int capacity, int userId)
    {
        var query = _db.ShiftCapacityOverrides
            .Where(o => o.ShiftTypeId == shiftTypeId
                && o.MoleculeId == moleculeId
                && o.Date == date);

        if (jobTypeId.HasValue)
            query = query.Where(o => o.JobTypeId == jobTypeId.Value);
        else
            query = query.Where(o => o.JobTypeId == null);

        var existing = await query.FirstOrDefaultAsync();

        if (existing != null)
        {
            existing.Capacity = capacity;
            existing.CreatedByUserId = userId;
            existing.CreatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.ShiftCapacityOverrides.Add(new ShiftCapacityOverride
            {
                ShiftTypeId = shiftTypeId,
                MoleculeId = moleculeId,
                JobTypeId = jobTypeId,
                Date = date,
                Capacity = capacity,
                CreatedByUserId = userId
            });
        }

        await _db.SaveChangesAsync();
    }

    public async Task RemoveCapacityOverrideAsync(int shiftTypeId, int moleculeId, int? jobTypeId, DateOnly date)
    {
        var query = _db.ShiftCapacityOverrides
            .Where(o => o.ShiftTypeId == shiftTypeId
                && o.MoleculeId == moleculeId
                && o.Date == date);

        if (jobTypeId.HasValue)
            query = query.Where(o => o.JobTypeId == jobTypeId.Value);
        else
            query = query.Where(o => o.JobTypeId == null);

        var existing = await query.FirstOrDefaultAsync();

        if (existing != null)
        {
            _db.ShiftCapacityOverrides.Remove(existing);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<List<AppUser>> GetEligibleUsersForShiftTypeAsync(int moleculeId, int shiftTypeId)
    {
        // SECURITY-AUDITED: SAFE — scoped by moleculeId + shiftTypeId; eligibility filters applied
        var shiftType = await _db.ShiftTypes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(st => st.Id == shiftTypeId);

        if (shiftType == null) return new List<AppUser>();

        var query = _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.IsActive
                && _db.Companies.Any(c => c.Id == u.CompanyId && c.MoleculeId == moleculeId));

        // Apply company eligibility filter
        var eligibleCompanyIds = shiftType.GetEligibleCompanyIdList();
        if (eligibleCompanyIds != null)
            query = query.Where(u => eligibleCompanyIds.Contains(u.CompanyId));

        // Apply officer rank filter (SegenMishne = 9)
        if (shiftType.RequiresOfficerRank)
            query = query.Where(u => (int)u.Rank >= 9);

        return await query
            .Include(u => u.JobType)
            .OrderBy(u => u.DisplayName)
            .ToListAsync();
    }

    public async Task<AssignmentResult> AssignUserAsync(int shiftInstanceId, int userId, int assignedByUserId)
    {
        var shiftInstance = await _db.ShiftInstances
            .Include(si => si.ShiftType)
            .FirstOrDefaultAsync(si => si.Id == shiftInstanceId);

        if (shiftInstance == null)
            return new AssignmentResult(false, "Shift instance not found", new());

        // SECURITY-AUDITED: SAFE — scoped by specific userId; molecule membership validated below
        var user = await _db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
            return new AssignmentResult(false, "User not found", new());

        var userCompany = await _companyCacheService.GetCompanyAsync(user.CompanyId);

        if (userCompany?.MoleculeId != shiftInstance.ShiftType.MoleculeId)
            return new AssignmentResult(false, "User does not belong to this molecule", new());

        // Check for rest violations (warning only, never blocks)
        var warnings = await CheckRestViolationsAsync(userId, shiftInstance.WorkDate, shiftInstance.ShiftTypeId);

        // Atomic assignment: wrap duplicate check + insert in a single transaction (fixes A-02)
        // SQLite serializes writes, so this transaction ensures the check-then-insert is atomic
        using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            // Check for duplicate assignment within the transaction
            var alreadyAssigned = await _db.ShiftAssignments
                .AnyAsync(a => a.ShiftInstanceId == shiftInstanceId && a.UserId == userId);

            if (alreadyAssigned)
            {
                await transaction.RollbackAsync();
                return new AssignmentResult(false, "User is already assigned to this shift", new());
            }

            var assignment = new ShiftAssignment
            {
                ShiftInstanceId = shiftInstanceId,
                UserId = userId,
                CompanyId = shiftInstance.CompanyId,
                CreatedAt = DateTime.UtcNow
            };

            _db.ShiftAssignments.Add(assignment);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Failed to atomically assign user {UserId} to shift {ShiftInstanceId}", userId, shiftInstanceId);
            return new AssignmentResult(false, "Failed to assign user - please try again", new());
        }

        _logger.LogInformation(
            "User {UserId} assigned to shift instance {ShiftInstanceId} by user {AssignedByUserId}",
            userId, shiftInstanceId, assignedByUserId);

        return new AssignmentResult(true, null, warnings);
    }

    public async Task<bool> UnassignUserAsync(int shiftAssignmentId, int unassignedByUserId)
    {
        var assignment = await _db.ShiftAssignments.FindAsync(shiftAssignmentId);
        if (assignment == null)
            return false;

        _db.ShiftAssignments.Remove(assignment);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Assignment {ShiftAssignmentId} removed by user {UnassignedByUserId}",
            shiftAssignmentId, unassignedByUserId);

        return true;
    }

    public async Task<List<RestViolationWarning>> CheckRestViolationsAsync(int userId, DateOnly date, int shiftTypeId)
    {
        var warnings = new List<RestViolationWarning>();

        // SECURITY-AUDITED: SAFE — scoped by specific shiftTypeId; used for rest hours calculation
        var targetShiftType = await _db.ShiftTypes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(st => st.Id == shiftTypeId);
        if (targetShiftType == null)
            return warnings;

        // Check previous day's shifts
        var previousDate = date.AddDays(-1);
        var previousAssignments = await _db.ShiftAssignments
            .Include(sa => sa.ShiftInstance)
                .ThenInclude(si => si.ShiftType)
            .Where(sa => sa.UserId == userId && sa.ShiftInstance.WorkDate == previousDate)
            .ToListAsync();

        foreach (var prevAssignment in previousAssignments)
        {
            var prevEnd = prevAssignment.ShiftInstance.ShiftType.End;
            var targetStart = targetShiftType.Start;

            // Calculate rest duration (handling overnight shifts)
            var restHours = CalculateRestHours(prevEnd, targetStart);

            if (restHours < DEFAULT_REST_HOURS)
            {
                var prevShiftTypeName = await _localizationService.ResolveShiftTypeNameAsync(prevAssignment.ShiftInstance.ShiftType, prevAssignment.CompanyId, CultureInfo.CurrentUICulture.Name);
                warnings.Add(new RestViolationWarning(
                    prevShiftTypeName,
                    previousDate,
                    TimeSpan.FromHours(restHours)
                ));
            }
        }

        return warnings;
    }

    private double CalculateRestHours(TimeOnly previousEnd, TimeOnly nextStart)
    {
        // Simple calculation - assumes next day
        var endDateTime = DateTime.Today.Add(previousEnd.ToTimeSpan());
        var startDateTime = DateTime.Today.AddDays(1).Add(nextStart.ToTimeSpan());
        return (startDateTime - endDateTime).TotalHours;
    }

    public async Task<Dictionary<(int UserId, DateOnly Date), FyiOverlayData>> GetOverlaysAsync(int moleculeId, DateOnly start, DateOnly end)
    {
        var result = new Dictionary<(int UserId, DateOnly Date), FyiOverlayData>();

        // C-07 OPTIMIZED: Single query for userIds using subquery join instead of two separate queries
        // SECURITY-AUDITED: SAFE — re-scoped by molecule membership
        var userIds = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.IsActive
                && _db.Companies.Any(c => c.Id == u.CompanyId && c.MoleculeId == moleculeId))
            .Select(u => u.Id)
            .ToListAsync();

        if (!userIds.Any())
            return result;

        // Load all overlay data in parallel (4 independent queries)
        // SECURITY-AUDITED: SAFE — all scoped by molecule-derived userIds + date range
        var timeOffTask = _db.TimeOffRequests
            .IgnoreQueryFilters()
            .Where(t => userIds.Contains(t.UserId)
                && t.StartDate <= end
                && t.EndDate >= start
                && t.Status == RequestStatus.Approved)
            .ToListAsync();

        var choresTask = _db.Chores
            .IgnoreQueryFilters()
            .Include(c => c.ChoreType)
            .Where(c => userIds.Contains(c.UserId)
                && c.Date >= start
                && c.Date <= end
                && c.CanceledAt == null)
            .ToListAsync();

        var onDutiesTask = _db.OnDuties
            .IgnoreQueryFilters()
            .Where(od => userIds.Contains(od.UserId)
                && od.Date >= start
                && od.Date <= end
                && od.CanceledAt == null)
            .ToListAsync();

        // Load custom duty type configs for name/color resolution
        var dutyTypeConfigsTask = _db.Set<OnDutyTypeConfig>()
            .Where(c => c.IsActive)
            .ToListAsync();

        // C-07 OPTIMIZED: Slim projection (UserId, WorkDate, CompanyId, ShiftTypeId) — ShiftType entities
        // loaded separately by distinct ID to avoid repeating ShiftType columns per assignment row
        var shiftsTask = _db.ShiftAssignments
            .IgnoreQueryFilters()
            .Where(sa => sa.UserId != null
                && userIds.Contains(sa.UserId.Value)
                && sa.ShiftInstance.WorkDate >= start
                && sa.ShiftInstance.WorkDate <= end)
            .Select(sa => new
            {
                UserId = sa.UserId!.Value,
                sa.ShiftInstance.WorkDate,
                CompanyId = sa.CompanyId,
                ShiftTypeId = sa.ShiftInstance.ShiftTypeId
            })
            .ToListAsync();

        await Task.WhenAll(timeOffTask, choresTask, onDutiesTask, shiftsTask, dutyTypeConfigsTask);

        var timeOffRequests = timeOffTask.Result;
        var chores = choresTask.Result;
        var onDuties = onDutiesTask.Result;
        var rawShifts = shiftsTask.Result;
        var dutyTypeConfigs = dutyTypeConfigsTask.Result;

        // C-07 OPTIMIZED: Load distinct ShiftType entities in one query, then resolve names
        var distinctShiftTypeIds = rawShifts.Select(s => s.ShiftTypeId).Distinct().ToList();
        var shiftTypeMap = await _db.ShiftTypes
            .IgnoreQueryFilters()
            .Where(st => distinctShiftTypeIds.Contains(st.Id))
            .ToDictionaryAsync(st => st.Id);

        var culture = CultureInfo.CurrentUICulture.Name;
        var shifts = new List<(int UserId, DateOnly WorkDate, string ShiftTypeName)>();
        foreach (var s in rawShifts)
        {
            var shiftType = shiftTypeMap.GetValueOrDefault(s.ShiftTypeId);
            var name = shiftType != null
                ? await _localizationService.ResolveShiftTypeNameAsync(shiftType, s.CompanyId, culture)
                : s.ShiftTypeId.ToString();
            shifts.Add((s.UserId, s.WorkDate, name));
        }

        // Build lookup dictionaries for O(1) access
        var choreLookup = chores.ToLookup(c => (c.UserId, c.Date));
        var onDutyLookup = onDuties.ToLookup(od => (od.UserId, od.Date));
        var shiftLookup = shifts.ToLookup(s => (s.UserId, s.WorkDate));
        var dutyConfigByType = dutyTypeConfigs.ToDictionary(c => c.TypeValue);

        // Build overlay data for each user-date combination
        foreach (var userId in userIds)
        {
            for (var date = start; date <= end; date = date.AddDays(1))
            {
                // Vacation/After render as the palm-tree badge; "Day at [X]" (DayAt) renders as its own
                // "יום {Label}" badge and must NOT be folded into HasVacation (Issue: day-X showed as vacation).
                var dayTimeOff = timeOffRequests
                    .Where(t => t.UserId == userId && t.StartDate <= date && t.EndDate >= date)
                    .ToList();
                var hasVacation = dayTimeOff.Any(t => t.Type != TimeOffType.DayAt);
                var dayAtLabel = dayTimeOff.FirstOrDefault(t => t.Type == TimeOffType.DayAt)?.Label;

                var choreItems = choreLookup[(userId, date)]
                    .Select(c =>
                    {
                        var name = c.ChoreType?.DisplayName ?? c.Title;
                        if (c.StartTime.HasValue && c.EndTime.HasValue)
                            name += $" {c.StartTime.Value:HH:mm}-{c.EndTime.Value:HH:mm}";
                        return new FyiOverlayItem(name, c.ChoreType?.Color);
                    })
                    .ToList();

                var onDutyItems = onDutyLookup[(userId, date)]
                    .Select(od =>
                    {
                        var typeValue = (int)od.Type;
                        if (dutyConfigByType.TryGetValue(typeValue, out var cfg))
                            return new FyiOverlayItem(cfg.NameHe, cfg.Color);
                        // Built-in types fallback
                        return od.Type switch
                        {
                            OnDutyType.Hakam => new FyiOverlayItem("חק\"מ", "#8B4513"),
                            OnDutyType.Lead => new FyiOverlayItem("מוביל", "#4A5568"),
                            _ => new FyiOverlayItem($"כוננות {typeValue}", null)
                        };
                    })
                    .ToList();

                var otherShifts = shiftLookup[(userId, date)]
                    .Select(s => s.ShiftTypeName)
                    .ToList();

                if (hasVacation || dayAtLabel != null || choreItems.Count > 0 || onDutyItems.Count > 0 || otherShifts.Any())
                {
                    result[(userId, date)] = new FyiOverlayData(hasVacation, choreItems, onDutyItems, otherShifts, dayAtLabel);
                }
            }
        }

        return result;
    }
}
