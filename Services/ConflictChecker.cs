using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

public class ConflictChecker : IConflictChecker
{
    private readonly AppDbContext _db;
    private readonly IAppConfigCacheService _configCache;
    private readonly ILogger<ConflictChecker> _logger;

    public ConflictChecker(AppDbContext db, IAppConfigCacheService configCache, ILogger<ConflictChecker> logger)
    {
        _db = db;
        _configCache = configCache;
        _logger = logger;
    }

    public async Task<ConflictResult> CanAssignAsync(int userId, ShiftInstance instance, CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync(new object?[] { userId }, ct);
        if (user == null || !user.IsActive)
            return LogAndFail(userId, instance.Id, "User inactive or not found.", "CONFLICT_USER_INACTIVE");

        var t = await _db.ShiftTypes.FindAsync(new object?[] { instance.ShiftTypeId }, ct);
        if (t is null) return LogAndFail(userId, instance.Id, "Shift type missing.", "CONFLICT_SHIFT_TYPE_MISSING");

        // OFFLINE shifts can coexist with other shifts - show warning but allow
        bool isOfflineShift = t.IsOffline;

        // Approved Time off blocks
        bool hasTimeOff = await _db.TimeOffRequests
            .AnyAsync(r => r.UserId == userId
                        && r.Status == RequestStatus.Approved
                        && instance.WorkDate >= r.StartDate
                        && instance.WorkDate <= r.EndDate, ct);
        if (hasTimeOff) return LogAndFail(userId, instance.Id, "Approved time-off covers this date.", "CONFLICT_TIME_OFF");

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
                                             st.End,
                                             st.IsOffline
                                         }).ToListAsync(ct);

        foreach (var ra in relevantAssignments)
        {
            var (rs, re) = TimeHelpers.GetShiftWindow(new ShiftType { Start = ra.Start, End = ra.End }, ra.WorkDate);
            // Overlap detection
            bool overlaps = rs < end && start < re;
            if (overlaps)
            {
                // A-01: Offline shifts (new or existing) can coexist — skip overlap block
                if (!isOfflineShift && !ra.IsOffline)
                {
                    return LogAndFail(userId, instance.Id, "Overlap with existing assignment.", "CONFLICT_OVERLAP");
                }
            }
        }

        // A-01: Rest period checks — exclude offline shifts from rest-period calculations
        // Offline shifts don't impose rest-period constraints on adjacent shifts
        var nonOfflineWindows = relevantAssignments
            .Where(ra => !ra.IsOffline)
            .Select(ra => TimeHelpers.GetShiftWindow(new ShiftType { Start = ra.Start, End = ra.End }, ra.WorkDate))
            .ToList();

        if (!isOfflineShift)
        {
            var before = nonOfflineWindows
                .Where(w => w.end <= start)
                .OrderByDescending(w => w.end)
                .FirstOrDefault();

            var after = nonOfflineWindows
                .Where(w => w.start >= end)
                .OrderBy(w => w.start)
                .FirstOrDefault();

            int restHours = await GetConfigIntAsync(instance.CompanyId, "RestHours", 8, ct);
            if (before.end != default && (start - before.end).TotalHours < restHours)
                return LogAndFail(userId, instance.Id, $"Rest period too short (< {restHours}h) from previous shift.", "CONFLICT_REST_PERIOD");
            if (after.start != default && (after.start - end).TotalHours < restHours)
                return LogAndFail(userId, instance.Id, $"Rest period too short (< {restHours}h) before next shift.", "CONFLICT_REST_PERIOD");
        }

        // Weekly cap: hours of existing week + this shift <= cap
        // A-02: Deduplicate overlapping time windows before summing to prevent double-counting
        // A-05: Use configurable week start day (0=Sunday for IDF, 1=Monday default)
        var weekStartDay = await GetConfigIntAsync(instance.CompanyId, "WeekStartDay", 0, ct);
        var weekStart2 = TimeHelpers.WeekStart(instance.WorkDate, (DayOfWeek)Math.Clamp(weekStartDay, 0, 6));
        var weekEnd2 = weekStart2.AddDays(6);
        var weekAssignments = await (from a in _db.ShiftAssignments
                                     join si in _db.ShiftInstances on a.ShiftInstanceId equals si.Id
                                     join st in _db.ShiftTypes on si.ShiftTypeId equals st.Id
                                     where a.UserId == userId
                                        && si.WorkDate >= weekStart2 && si.WorkDate <= weekEnd2
                                     select new { si.WorkDate, st.Start, st.End })
                                     .ToListAsync(ct);

        var weekWindows = weekAssignments
            .Select(ra => TimeHelpers.GetShiftWindow(new ShiftType { Start = ra.Start, End = ra.End }, ra.WorkDate))
            .OrderBy(w => w.start)
            .ToList();

        double totalHoursThisWeek = MergeAndSumHours(weekWindows);

        totalHoursThisWeek += TimeHelpers.Hours(t);

        int weeklyCap = await GetConfigIntAsync(instance.CompanyId, "WeeklyHoursCap", 40, ct);
        if (totalHoursThisWeek > weeklyCap)
            return LogAndFail(userId, instance.Id, $"Weekly hours cap exceeded (> {weeklyCap}h).", "CONFLICT_WEEKLY_CAP");

        return ConflictResult.Ok();
    }

    /// <summary>
    /// A-02: Merges overlapping time windows and sums total unique hours.
    /// Prevents double-counting when shifts overlap (e.g., two overlapping 8hr offline shifts
    /// should count as 8hr, not 16hr).
    /// </summary>
    private static double MergeAndSumHours(List<(DateTime start, DateTime end)> windows)
    {
        if (windows.Count == 0) return 0;

        double total = 0;
        var currentStart = windows[0].start;
        var currentEnd = windows[0].end;

        for (int i = 1; i < windows.Count; i++)
        {
            if (windows[i].start <= currentEnd)
            {
                // Overlapping — extend the merged window
                if (windows[i].end > currentEnd)
                    currentEnd = windows[i].end;
            }
            else
            {
                // No overlap — add the merged window and start a new one
                total += (currentEnd - currentStart).TotalHours;
                currentStart = windows[i].start;
                currentEnd = windows[i].end;
            }
        }

        total += (currentEnd - currentStart).TotalHours;
        return total;
    }

    /// <summary>
    /// F-01: Log every conflict failure with structured data for metrics/monitoring.
    /// Use grep for "ConflictBlocked" to count and categorize failures.
    /// </summary>
    private ConflictResult LogAndFail(int userId, int shiftInstanceId, string reason, string errorCode)
    {
        _logger.LogWarning("ConflictBlocked: UserId={UserId} ShiftInstanceId={ShiftInstanceId} ErrorCode={ErrorCode} Reason={Reason}",
            userId, shiftInstanceId, errorCode, reason);
        return ConflictResult.Fail(reason, errorCode);
    }

    // PERFORMANCE FIX: Use config cache to reduce database queries
    private async Task<int> GetConfigIntAsync(int companyId, string key, int defaultValue, CancellationToken ct = default)
    {
        var config = await _configCache.GetConfigAsync(companyId, key);
        return int.TryParse(config?.Value, out var i) ? i : defaultValue;
    }
}
