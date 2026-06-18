using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Schedule;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

/// <inheritdoc />
/// <remarks>
/// Query logic moved verbatim from <c>Pages/My/IndexModel.OnGetAsync</c> (Phase 6 Home-spine merge)
/// so the rich /My timeline is byte-identical and the Home spine reuses the exact same pipeline.
/// Item titles are intentionally preserved as the original (non-localized) strings to keep /My
/// unchanged; localizing them is a separate, pre-existing concern.
/// </remarks>
public class PersonalTimelineService : IPersonalTimelineService
{
    private readonly AppDbContext _db;

    public PersonalTimelineService(AppDbContext db) => _db = db;

    public async Task<PersonalTimeline> GetTimelineAsync(int userId, DateOnly rangeStart, DateOnly rangeEnd)
    {
        var emptyStats = new StatsData(0, 0, 0, 0, 0, 0, 0, 0);

        var currentUser = await _db.Users.FindAsync(userId);
        if (currentUser == null)
            return new PersonalTimeline(new List<TimelineItem>(), emptyStats);

        var items = new List<TimelineItem>();

        // 1. Shifts (the user's own assignments).
        var shifts = await (from a in _db.ShiftAssignments
                            join si in _db.ShiftInstances on a.ShiftInstanceId equals si.Id
                            join st in _db.ShiftTypes on si.ShiftTypeId equals st.Id
                            join t in _db.Users on a.TraineeUserId equals t.Id into traineeJoin
                            from trainee in traineeJoin.DefaultIfEmpty()
                            where a.UserId == userId && si.WorkDate >= rangeStart && si.WorkDate <= rangeEnd
                            select new
                            {
                                si.Id,
                                si.WorkDate,
                                st.Key,
                                st.Name,
                                st.Start,
                                st.End,
                                TraineeName = trainee != null ? trainee.DisplayName : null,
                                InstanceName = si.Name
                            }).ToListAsync();

        foreach (var shift in shifts)
        {
            var hours = TimeHelpers.Hours(new ShiftType { Start = shift.Start, End = shift.End });
            var title = string.IsNullOrEmpty(shift.InstanceName)
                ? (shift.Key.StartsWith("CUSTOM_") ? shift.Name : shift.Key)
                : shift.InstanceName;
            var timeRange = $"{shift.Start:HH:mm} - {shift.End:HH:mm}";
            var metadata = shift.TraineeName != null ? $"Training: {shift.TraineeName}" : $"{hours:F1} hours";

            items.Add(new TimelineItem(
                shift.Id, ItemType.Shift, shift.WorkDate, null,
                $"Shift — {title}", timeRange, metadata, false, hours));
        }

        // Trainees also see the shifts they shadow.
        if (currentUser.Role == UserRole.Trainee)
        {
            var shadowingShifts = await (from a in _db.ShiftAssignments
                                         join si in _db.ShiftInstances on a.ShiftInstanceId equals si.Id
                                         join st in _db.ShiftTypes on si.ShiftTypeId equals st.Id
                                         join u in _db.Users on a.UserId equals u.Id
                                         where a.TraineeUserId == userId && si.WorkDate >= rangeStart && si.WorkDate <= rangeEnd
                                         select new
                                         {
                                             si.Id,
                                             si.WorkDate,
                                             st.Key,
                                             st.Name,
                                             st.Start,
                                             st.End,
                                             EmployeeName = u.DisplayName,
                                             InstanceName = si.Name
                                         }).ToListAsync();

            foreach (var shadow in shadowingShifts)
            {
                var hours = TimeHelpers.Hours(new ShiftType { Start = shadow.Start, End = shadow.End });
                var title = string.IsNullOrEmpty(shadow.InstanceName)
                    ? (shadow.Key.StartsWith("CUSTOM_") ? shadow.Name : shadow.Key)
                    : shadow.InstanceName;
                var timeRange = $"{shadow.Start:HH:mm} - {shadow.End:HH:mm}";

                items.Add(new TimelineItem(
                    shadow.Id, ItemType.Shift, shadow.WorkDate, null,
                    $"Shift — {title} (Shadowing)", timeRange, $"Shadowing: {shadow.EmployeeName}",
                    false, hours, "shadowing"));
            }
        }

        // 2. Vacations (approved only).
        var vacations = await _db.TimeOffRequests
            .Where(r => r.UserId == userId &&
                        r.Status == RequestStatus.Approved &&
                        r.EndDate >= rangeStart &&
                        r.StartDate <= rangeEnd)
            .ToListAsync();

        foreach (var vacation in vacations)
        {
            var days = (vacation.EndDate.DayNumber - vacation.StartDate.DayNumber) + 1;
            var typeLabel = vacation.Type switch
            {
                TimeOffType.Vacation => "Vacation",
                TimeOffType.DayAt => string.IsNullOrWhiteSpace(vacation.Label) ? "Day" : $"{vacation.Label.Trim()} day",
                _ => "After"
            };
            var metadata = days == 1 ? "1 day" : $"{days} days";

            items.Add(new TimelineItem(
                vacation.Id, ItemType.Vacation, vacation.StartDate, vacation.EndDate,
                typeLabel, null, metadata, false, 0, vacation.Type.ToString()));
        }

        // 3. On-Duty (active only).
        var onDuties = await _db.OnDuties
            .Where(od => od.UserId == userId &&
                         od.CanceledAt == null &&
                         od.Date >= rangeStart &&
                         od.Date <= rangeEnd)
            .ToListAsync();

        foreach (var onDuty in onDuties)
        {
            var typeLabel = onDuty.Type == OnDutyType.Hakam ? "On-Duty Hakam" : "On-Duty Lead";
            var metadata = !string.IsNullOrEmpty(onDuty.Notes) ? onDuty.Notes : null;

            items.Add(new TimelineItem(
                onDuty.Id, ItemType.OnDuty, onDuty.Date, null,
                typeLabel, null, metadata, false, 0, onDuty.Type.ToString()));
        }

        // 4. Chores (active only).
        var chores = await _db.Chores
            .Where(c => c.UserId == userId &&
                        c.CanceledAt == null &&
                        c.Date >= rangeStart &&
                        c.Date <= rangeEnd)
            .ToListAsync();

        foreach (var chore in chores)
        {
            items.Add(new TimelineItem(
                chore.Id, ItemType.Chore, chore.Date, null,
                $"Chore: {chore.Title}", null, chore.Notes, false, 0, null));
        }

        // Conflict detection — items sharing a date.
        var itemsByDate = items.GroupBy(i => i.Date).Where(g => g.Count() > 1).ToList();
        if (itemsByDate.Any())
        {
            var conflictDates = itemsByDate.Select(g => g.Key).ToHashSet();
            items = items.Select(item => item with { HasConflict = conflictDates.Contains(item.Date) }).ToList();
        }

        var ordered = items.OrderBy(i => i.Date).ToList();

        var vacationItems = ordered.Where(i => i.Type == ItemType.Vacation).ToList();
        var onDutyItems = ordered.Where(i => i.Type == ItemType.OnDuty).ToList();
        var shiftItems = ordered.Where(i => i.Type == ItemType.Shift).ToList();
        var choreItems = ordered.Where(i => i.Type == ItemType.Chore).ToList();

        var stats = new StatsData(
            vacationItems.Count, vacationItems.Sum(i => i.Hours),
            onDutyItems.Count, onDutyItems.Sum(i => i.Hours),
            shiftItems.Count, shiftItems.Sum(i => i.Hours),
            choreItems.Count, choreItems.Sum(i => i.Hours));

        return new PersonalTimeline(ordered, stats);
    }
}
