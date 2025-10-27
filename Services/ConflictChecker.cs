using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

public class ConflictChecker : IConflictChecker
{
    private readonly AppDbContext _db;

    public ConflictChecker(AppDbContext db) => _db = db;

    public async Task<ConflictResult> CanAssignAsync(int userId, ShiftInstance instance, CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync(new object?[] { userId }, ct);
        if (user == null || !user.IsActive)
            return ConflictResult.Fail("User inactive or not found.");

        var t = await _db.ShiftTypes.FindAsync(new object?[] { instance.ShiftTypeId }, ct);
        if (t is null) return ConflictResult.Fail("Shift type missing.");

        // Approved Time off blocks
        bool hasTimeOff = await _db.TimeOffRequests
            .AnyAsync(r => r.UserId == userId
                        && r.Status == RequestStatus.Approved
                        && instance.WorkDate >= r.StartDate
                        && instance.WorkDate <= r.EndDate, ct);
        if (hasTimeOff) return ConflictResult.Fail("Approved time-off covers this date.");

        var (start, end) = TimeHelpers.GetShiftWindow(t, instance.WorkDate);

        // Overlap + Rest + Weekly cap checks
        // Fetch assignments in the surrounding 7 days for the user
        var weekStart = TimeHelpers.WeekStart(instance.WorkDate).AddDays(-1);
        var weekEnd = weekStart.AddDays(8);

        var relevantAssignments = await (from a in _db.ShiftAssignments
                                         join si in _db.ShiftInstances on a.ShiftInstanceId equals si.Id
                                         join st in _db.ShiftTypes on si.ShiftTypeId equals st.Id
                                         where a.UserId == userId
                                            && si.WorkDate >= weekStart && si.WorkDate <= weekEnd
                                         select new
                                         {
                                             si.WorkDate,
                                             st.Start,
                                             st.End
                                         }).ToListAsync(ct);

        double totalHoursThisWeek = 0;
        foreach (var ra in relevantAssignments)
        {
            var (rs, re) = TimeHelpers.GetShiftWindow(new ShiftType { Start = ra.Start, End = ra.End }, ra.WorkDate);
            // Overlap
            bool overlaps = rs < end && start < re;
            if (overlaps) return ConflictResult.Fail("Overlap with existing assignment.");
        }

        // Rest period: find nearest before/after shifts
        var before = relevantAssignments
            .Select(ra => TimeHelpers.GetShiftWindow(new ShiftType { Start = ra.Start, End = ra.End }, ra.WorkDate))
            .Where(w => w.end <= start)
            .OrderByDescending(w => w.end)
            .FirstOrDefault();

        var after = relevantAssignments
            .Select(ra => TimeHelpers.GetShiftWindow(new ShiftType { Start = ra.Start, End = ra.End }, ra.WorkDate))
            .Where(w => w.start >= end)
            .OrderBy(w => w.start)
            .FirstOrDefault();

        int restHours = await GetConfigIntAsync(instance.CompanyId, "RestHours", 8, ct);
        if (before.end != default && (start - before.end).TotalHours < restHours)
            return ConflictResult.Fail($"Rest period too short (< {restHours}h) from previous shift.");
        if (after.start != default && (after.start - end).TotalHours < restHours)
            return ConflictResult.Fail($"Rest period too short (< {restHours}h) before next shift.");

        // Weekly cap: hours of existing week + this shift <= cap
        var weekStart2 = TimeHelpers.WeekStart(instance.WorkDate);
        var weekEnd2 = weekStart2.AddDays(6);
        var weekAssignments = await (from a in _db.ShiftAssignments
                                     join si in _db.ShiftInstances on a.ShiftInstanceId equals si.Id
                                     join st in _db.ShiftTypes on si.ShiftTypeId equals st.Id
                                     where a.UserId == userId
                                        && si.WorkDate >= weekStart2 && si.WorkDate <= weekEnd2
                                     select new { si.WorkDate, st.Start, st.End })
                                     .ToListAsync(ct);

        foreach (var ra in weekAssignments)
        {
            var (rs, re) = TimeHelpers.GetShiftWindow(new ShiftType { Start = ra.Start, End = ra.End }, ra.WorkDate);
            totalHoursThisWeek += (re - rs).TotalHours;
        }

        totalHoursThisWeek += TimeHelpers.Hours(t);

        int weeklyCap = await GetConfigIntAsync(instance.CompanyId, "WeeklyHoursCap", 40, ct);
        if (totalHoursThisWeek > weeklyCap)
            return ConflictResult.Fail($"Weekly hours cap exceeded (> {weeklyCap}h).");

        return ConflictResult.Ok();
    }

    // PERFORMANCE FIX: Made async to prevent blocking thread pool
    private async Task<int> GetConfigIntAsync(int companyId, string key, int defaultValue, CancellationToken ct = default)
    {
        var config = await _db.Configs
            .Where(c => c.CompanyId == companyId && c.Key == key)
            .FirstOrDefaultAsync(ct);
        return int.TryParse(config?.Value, out var i) ? i : defaultValue;
    }
}
