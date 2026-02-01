using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

public interface IBusyUserService
{
    /// <summary>
    /// Get busy status for all users on a specific date and time range.
    /// Busy = has vacation, non-Offline shift, or chore overlapping the time window.
    /// </summary>
    Task<Dictionary<int, BusyStatus>> GetBusyUsersAsync(
        DateOnly date,
        TimeOnly start,
        TimeOnly end,
        int? excludeShiftTypeId = null);
}

public class BusyStatus
{
    public bool HasVacation { get; set; }
    public bool HasShift { get; set; }
    public bool HasChore { get; set; }
    public List<string> Reasons { get; set; } = new();

    /// <summary>
    /// CSS class for color coding: blue for vacation, red for shift, yellow for chore.
    /// Priority: vacation > shift > chore.
    /// </summary>
    public string CssClass
    {
        get
        {
            if (HasVacation) return "busy-vacation";
            if (HasShift) return "busy-shift";
            if (HasChore) return "busy-chore";
            return "";
        }
    }

    /// <summary>
    /// User-friendly display text like "Busy: vacation" or "Busy: shift and chore".
    /// </summary>
    public string DisplayText => string.Join(" & ", Reasons);

    public bool IsBusy => HasVacation || HasShift || HasChore;
}

public class BusyUserService : IBusyUserService
{
    private readonly AppDbContext _db;
    private readonly ICompanyContext _companyContext;

    public BusyUserService(AppDbContext db, ICompanyContext companyContext)
    {
        _db = db;
        _companyContext = companyContext;
    }

    public async Task<Dictionary<int, BusyStatus>> GetBusyUsersAsync(
        DateOnly date,
        TimeOnly start,
        TimeOnly end,
        int? excludeShiftTypeId = null)
    {
        var companyId = _companyContext.GetCompanyIdOrThrow();
        var result = new Dictionary<int, BusyStatus>();

        // Get all active users for this company
        var users = await _db.Users
            .Where(u => u.IsActive && u.CompanyId == companyId)
            .Select(u => u.Id)
            .ToListAsync();

        // Batch query for vacations with new time-based logic
        // Optimize: Add date range filter to reduce records loaded from database
        // We need to check a wider date range because:
        // - Vacation: EndDate+1 at 13:00 (so we need EndDate >= checkDate-1)
        // - After: StartDate+1 at 13:00 (so we need StartDate >= checkDate-1)
        // For the upper bound, StartDate should be <= checkDate+1 to catch "After" requests
        var dateRangeStart = date.AddDays(-1);
        var dateRangeEnd = date.AddDays(1);

        var potentialVacations = await _db.TimeOffRequests
            .Where(r => r.Status == RequestStatus.Approved
                     && r.CompanyId == companyId
                     && r.StartDate <= dateRangeEnd
                     && r.EndDate >= dateRangeStart)
            .ToListAsync();

        // Filter in memory using the new time-based logic
        var checkDateTime = date.ToDateTime(start);
        var checkEndDateTime = date.ToDateTime(end);

        var usersWithVacation = potentialVacations
            .Where(r =>
            {
                var vacationStart = r.GetActualStartDateTime();
                var vacationEnd = r.GetActualEndDateTime();

                // Check if there's any overlap between the check period and vacation period
                return checkDateTime < vacationEnd && checkEndDateTime > vacationStart;
            })
            .Select(r => r.UserId)
            .Distinct()
            .ToList();

        // Batch query for shifts (exclude Offline shifts as they don't block)
        var usersWithShifts = await (from a in _db.ShiftAssignments
                                     join si in _db.ShiftInstances on a.ShiftInstanceId equals si.Id
                                     join st in _db.ShiftTypes on si.ShiftTypeId equals st.Id
                                     where si.CompanyId == companyId &&
                                           si.WorkDate == date &&
                                           a.UserId != null &&
                                           st.Key != ShiftType.KEY_OFFLINE && // Offline doesn't count as busy (use Key instead of computed property)
                                           (excludeShiftTypeId == null || st.Id != excludeShiftTypeId) &&
                                           // Time overlap check (simplified - assumes same-day shifts)
                                           st.Start < end && start < st.End
                                     select a.UserId!.Value) // Null-forgiving operator: we checked != null above
                                     .Distinct()
                                     .ToListAsync();

        // Batch query for chores
        var usersWithChores = await _db.Chores
            .Where(c => c.CompanyId == companyId && c.Date == date && c.CanceledAt == null)
            .Select(c => c.UserId)
            .Distinct()
            .ToListAsync();

        // Build status dictionary
        foreach (var userId in users)
        {
            var status = new BusyStatus();

            if (usersWithVacation.Contains(userId))
            {
                status.HasVacation = true;
                status.Reasons.Add("vacation");
            }

            if (usersWithShifts.Contains(userId))
            {
                status.HasShift = true;
                status.Reasons.Add("shift");
            }

            if (usersWithChores.Contains(userId))
            {
                status.HasChore = true;
                status.Reasons.Add("chore");
            }

            if (status.IsBusy)
            {
                result[userId] = status;
            }
        }

        return result;
    }
}
