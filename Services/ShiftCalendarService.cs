// Services/ShiftCalendarService.cs
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
    private const int DEFAULT_REST_HOURS = 8;
    private const int DEFAULT_STAFFING_REQUIRED = 1;

    public ShiftCalendarService(AppDbContext db, ILogger<ShiftCalendarService> logger, ICompanyCacheService companyCacheService)
    {
        _db = db;
        _logger = logger;
        _companyCacheService = companyCacheService;
    }

    public async Task<List<AppUser>> GetUsersForCalendarAsync(int moleculeId, int jobTypeId)
    {
        // C-07 OPTIMIZED: Single query with join instead of two separate queries
        // SECURITY-AUDITED: SAFE — re-scoped by molecule membership + jobTypeId
        return await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.IsActive
                && u.JobTypeId == jobTypeId
                && _db.Companies.Any(c => c.Id == u.CompanyId && c.MoleculeId == moleculeId))
            .Include(u => u.JobType)
            .OrderBy(u => u.DisplayName)
            .ToListAsync();
    }

    public async Task<List<ShiftInstance>> GetShiftInstancesAsync(int moleculeId, int jobTypeId, DateOnly start, DateOnly end)
    {
        // SECURITY-AUDITED: SAFE — scoped by moleculeId + jobTypeId + date range
        return await _db.ShiftInstances
            .IgnoreQueryFilters()
            .Include(si => si.ShiftType)
            .Where(si => si.ShiftType.MoleculeId == moleculeId
                && si.ShiftType.JobTypeId == jobTypeId
                && si.WorkDate >= start
                && si.WorkDate <= end)
            .ToListAsync();
    }

    public async Task<List<ShiftAssignment>> GetAssignmentsAsync(int moleculeId, int jobTypeId, DateOnly start, DateOnly end)
    {
        // SECURITY-AUDITED: SAFE — scoped by moleculeId + jobTypeId + date range
        return await _db.ShiftAssignments
            .IgnoreQueryFilters()
            .Include(sa => sa.User)
            .Include(sa => sa.Trainee)
            .Include(sa => sa.ShiftInstance)
                .ThenInclude(si => si.ShiftType)
            .AsSplitQuery() // C-07: split multi-include query to avoid cartesian explosion
            .Where(sa => sa.ShiftInstance.ShiftType.MoleculeId == moleculeId
                && sa.ShiftInstance.ShiftType.JobTypeId == jobTypeId
                && sa.ShiftInstance.WorkDate >= start
                && sa.ShiftInstance.WorkDate <= end)
            .ToListAsync();
    }

    public async Task<int> GetCapacityAsync(int shiftTypeId, int moleculeId, int jobTypeId, DateOnly date)
    {
        var overrideCapacity = await _db.ShiftCapacityOverrides
            .Where(o => o.ShiftTypeId == shiftTypeId
                && o.MoleculeId == moleculeId
                && o.JobTypeId == jobTypeId
                && o.Date == date)
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
        int moleculeId, int jobTypeId, DateOnly start, DateOnly end)
    {
        var result = new Dictionary<(int ShiftTypeId, DateOnly Date), int>();

        // 1) Batch-load all capacity overrides for this scope and date range (single query)
        var overrides = await _db.ShiftCapacityOverrides
            .Where(o => o.MoleculeId == moleculeId
                && o.JobTypeId == jobTypeId
                && o.Date >= start
                && o.Date <= end)
            .ToListAsync();

        var overrideLookup = overrides.ToLookup(o => (o.ShiftTypeId, o.Date));

        // 2) Batch-load all shift instances to get default StaffingRequired (single query)
        // SECURITY-AUDITED: SAFE — scoped by moleculeId + jobTypeId + date range
        var instances = await _db.ShiftInstances
            .IgnoreQueryFilters()
            .Include(si => si.ShiftType)
            .Where(si => si.ShiftType.MoleculeId == moleculeId
                && si.ShiftType.JobTypeId == jobTypeId
                && si.WorkDate >= start
                && si.WorkDate <= end)
            .ToListAsync();

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

    public async Task SetCapacityOverrideAsync(int shiftTypeId, int moleculeId, int jobTypeId, DateOnly date, int capacity, int userId)
    {
        var existing = await _db.ShiftCapacityOverrides
            .FirstOrDefaultAsync(o => o.ShiftTypeId == shiftTypeId
                && o.MoleculeId == moleculeId
                && o.JobTypeId == jobTypeId
                && o.Date == date);

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

    public async Task RemoveCapacityOverrideAsync(int shiftTypeId, int moleculeId, int jobTypeId, DateOnly date)
    {
        var existing = await _db.ShiftCapacityOverrides
            .FirstOrDefaultAsync(o => o.ShiftTypeId == shiftTypeId
                && o.MoleculeId == moleculeId
                && o.JobTypeId == jobTypeId
                && o.Date == date);

        if (existing != null)
        {
            _db.ShiftCapacityOverrides.Remove(existing);
            await _db.SaveChangesAsync();
        }
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
                warnings.Add(new RestViolationWarning(
                    prevAssignment.ShiftInstance.ShiftType.Name,
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

        // C-07 OPTIMIZED: Use projection for shifts overlay — only need UserId, WorkDate, ShiftTypeName
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
                ShiftTypeName = sa.ShiftInstance.ShiftType.Name
            })
            .ToListAsync();

        await Task.WhenAll(timeOffTask, choresTask, onDutiesTask, shiftsTask);

        var timeOffRequests = timeOffTask.Result;
        var chores = choresTask.Result;
        var onDuties = onDutiesTask.Result;
        var shifts = shiftsTask.Result;

        // Build lookup dictionaries for O(1) access
        var choreLookup = chores.ToLookup(c => (c.UserId, c.Date));
        var onDutyLookup = onDuties.ToLookup(od => (od.UserId, od.Date));
        var shiftLookup = shifts.ToLookup(s => (s.UserId, s.WorkDate));

        // Build overlay data for each user-date combination
        foreach (var userId in userIds)
        {
            for (var date = start; date <= end; date = date.AddDays(1))
            {
                var hasVacation = timeOffRequests.Any(t => t.UserId == userId && t.StartDate <= date && t.EndDate >= date);
                var hasChore = choreLookup[(userId, date)].Any();
                var hasOnDuty = onDutyLookup[(userId, date)].Any();
                var otherShifts = shiftLookup[(userId, date)]
                    .Select(s => s.ShiftTypeName)
                    .ToList();

                if (hasVacation || hasChore || hasOnDuty || otherShifts.Any())
                {
                    result[(userId, date)] = new FyiOverlayData(hasVacation, hasChore, hasOnDuty, otherShifts);
                }
            }
        }

        return result;
    }
}
