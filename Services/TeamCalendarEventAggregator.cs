using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Services;

/// <summary>
/// Aggregates events from multiple sources (vacations, shifts, chores, on-duty)
/// and computes the single highest-priority status for each member on each day.
/// </summary>
public class TeamCalendarEventAggregator
{
    private readonly AppDbContext _context;
    private readonly ICompanyLocalizationService _localizationService;

    public TeamCalendarEventAggregator(AppDbContext context, ICompanyLocalizationService localizationService)
    {
        _context = context;
        _localizationService = localizationService;
    }

    /// <summary>
    /// Gets the week view data for a team calendar.
    /// Returns a dictionary: MemberUserId -> DayOfWeek -> DayStatus
    /// </summary>
    public async Task<Dictionary<int, Dictionary<DayOfWeek, DayStatus>>> GetWeekViewAsync(
        List<int> memberUserIds,
        DateOnly weekStart) // Should be a Sunday
    {
        var weekEnd = weekStart.AddDays(6); // Saturday

        // Fetch all events for the week for all members
        var vacations = await GetVacationsAsync(memberUserIds, weekStart, weekEnd);
        var shifts = await GetShiftsAsync(memberUserIds, weekStart, weekEnd);
        var chores = await GetChoresAsync(memberUserIds, weekStart, weekEnd);
        var onDutyAssignments = await GetOnDutyAsync(memberUserIds, weekStart, weekEnd);

        // Build the result dictionary
        var result = new Dictionary<int, Dictionary<DayOfWeek, DayStatus>>();

        foreach (var userId in memberUserIds)
        {
            var userWeek = new Dictionary<DayOfWeek, DayStatus>();

            for (int i = 0; i < 7; i++)
            {
                var date = weekStart.AddDays(i);
                var dayOfWeek = date.DayOfWeek;

                var status = ComputeDayStatus(
                    userId,
                    date,
                    vacations,
                    shifts,
                    chores,
                    onDutyAssignments);

                userWeek[dayOfWeek] = status;
            }

            result[userId] = userWeek;
        }

        return result;
    }

    /// <summary>
    /// Computes the single status for a user on a specific day.
    /// Priority: Vacation > After > On-Duty > Home > Shift > Chore > Free
    /// (Home is promoted above plain Shift so the unified HOME marker wins
    /// when a user has a HOME assignment — Task 24.)
    /// </summary>
    private DayStatus ComputeDayStatus(
        int userId,
        DateOnly date,
        List<VacationEvent> vacations,
        List<ShiftEvent> shifts,
        List<ChoreEvent> chores,
        List<OnDutyEvent> onDutyAssignments)
    {
        // Check Vacation (highest priority)
        var vacation = GetVacationForDay(userId, date, vacations);
        if (vacation != null)
        {
            return vacation;
        }

        // Check On-Duty (second highest, includes After which is a type of On-Duty)
        var onDuty = GetOnDutyForDay(userId, date, onDutyAssignments);
        if (onDuty != null)
        {
            return onDuty;
        }

        // Task 24: HOME wins over plain Shift so the unified HOME indicator
        // appears on MyTeam when a user has a HOME (rotation/vacation/after) day.
        var home = GetHomeForDay(userId, date, shifts);
        if (home != null)
        {
            return home;
        }

        // Check Shift
        var shift = GetShiftForDay(userId, date, shifts);
        if (shift != null)
        {
            return shift;
        }

        // Check Chore
        var chore = GetChoreForDay(userId, date, chores);
        if (chore != null)
        {
            return chore;
        }

        // Default: Free
        return new DayStatus
        {
            Type = DayStatusType.Free,
            Label = "Free"
        };
    }

    #region Vacation/After Logic (TimeOffRequest)

    private DayStatus? GetVacationForDay(int userId, DateOnly date, List<VacationEvent> vacations)
    {
        var vacation = vacations.FirstOrDefault(v => v.UserId == userId && DateIsInTimeOffRange(date, v));

        if (vacation == null)
        {
            return null;
        }

        // Handle After type (אפטר)
        if (vacation.Type == TimeOffType.After)
        {
            var afterPartialType = GetAfterPartialType(date, vacation);

            switch (afterPartialType)
            {
                case AfterPartialType.From4PM:
                    return new DayStatus
                    {
                        Type = DayStatusType.AfterPartial,
                        Label = "After from 4PM",
                        TimeRange = "From 16:00",
                        Metadata = "After",
                        TargetUrl = "/Admin/TimeOff"
                    };

                case AfterPartialType.Until1PM:
                    return new DayStatus
                    {
                        Type = DayStatusType.AfterPartial,
                        Label = "After until 1PM",
                        TimeRange = "Until 13:00",
                        Metadata = "After",
                        TargetUrl = "/Admin/TimeOff"
                    };

                default:
                    return null;
            }
        }

        // Handle "Day at [X]" (Issue 4): same date window as Vacation, but render the free-text label
        // as "{X} day" / "יום {X}". Reuses the Vacation partial-day logic for full-day vs 1PM extension.
        if (vacation.Type == TimeOffType.DayAt)
        {
            var dayAtLabel = BuildDayAtLabel(vacation.Label);
            return GetVacationPartialType(date, vacation) switch
            {
                VacationPartialType.FullDay => new DayStatus
                {
                    Type = DayStatusType.Vacation,
                    Label = dayAtLabel,
                    TimeRange = null,
                    Metadata = "DayAt",
                    TargetUrl = "/Admin/TimeOff"
                },
                VacationPartialType.ExtensionUntil1PM => new DayStatus
                {
                    Type = DayStatusType.VacationPartial,
                    Label = dayAtLabel,
                    TimeRange = "Until 13:00",
                    Metadata = "DayAt",
                    TargetUrl = "/Admin/TimeOff"
                },
                _ => null
            };
        }

        // Handle Vacation type
        var vacationPartialType = GetVacationPartialType(date, vacation);

        switch (vacationPartialType)
        {
            case VacationPartialType.FullDay:
                return new DayStatus
                {
                    Type = DayStatusType.Vacation,
                    Label = "Vacation",
                    TimeRange = null,
                    Metadata = vacation.Type.ToString(),
                    TargetUrl = "/Admin/TimeOff"
                };

            case VacationPartialType.ExtensionUntil1PM:
                return new DayStatus
                {
                    Type = DayStatusType.VacationPartial,
                    Label = "Vacation until 1PM",
                    TimeRange = "Until 13:00",
                    Metadata = vacation.Type.ToString(),
                    TargetUrl = "/Admin/TimeOff"
                };

            default:
                return null;
        }
    }

    /// <summary>
    /// Builds the calendar label for a "Day at [X]" request: "{X} day" (English) / "יום {X}" (Hebrew).
    /// Culture-aware via CurrentUICulture (set per-request). Falls back to a bare "Day"/"יום" if no label.
    /// The free-text portion (X) is user-supplied and intentionally not localized.
    /// </summary>
    private static string BuildDayAtLabel(string? label)
    {
        var x = (label ?? string.Empty).Trim();
        var isHe = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "he";
        if (string.IsNullOrEmpty(x)) return isHe ? "יום" : "Day";
        return isHe ? $"יום {x}" : $"{x} day";
    }

    /// <summary>
    /// Checks if a date falls within a time-off request's effective range.
    /// Vacation semantics: covers full days + busy until 13:00 the day after EndDate.
    /// After semantics: 4PM on StartDate → 1PM on day after StartDate (single day).
    /// </summary>
    private bool DateIsInTimeOffRange(DateOnly date, VacationEvent vacation)
    {
        if (vacation.Type == TimeOffType.After)
        {
            // After: StartDate (from 4PM) and StartDate+1 (until 1PM)
            return date == vacation.StartDate || date == vacation.StartDate.AddDays(1);
        }
        else
        {
            // Vacation: Full vacation days
            if (date >= vacation.StartDate && date <= vacation.EndDate)
            {
                return true;
            }

            // Extension day (day after EndDate, busy until 1PM)
            if (date == vacation.EndDate.AddDays(1))
            {
                return true;
            }

            return false;
        }
    }

    private enum VacationPartialType
    {
        None,
        FullDay,
        ExtensionUntil1PM
    }

    private VacationPartialType GetVacationPartialType(DateOnly date, VacationEvent vacation)
    {
        // Full vacation days
        if (date >= vacation.StartDate && date <= vacation.EndDate)
        {
            return VacationPartialType.FullDay;
        }

        // Extension day (day after EndDate, busy until 1PM)
        if (date == vacation.EndDate.AddDays(1))
        {
            return VacationPartialType.ExtensionUntil1PM;
        }

        return VacationPartialType.None;
    }

    private enum AfterPartialType
    {
        None,
        From4PM,
        Until1PM
    }

    private AfterPartialType GetAfterPartialType(DateOnly date, VacationEvent vacation)
    {
        // Start day (from 4PM)
        if (date == vacation.StartDate)
        {
            return AfterPartialType.From4PM;
        }

        // Extension day (day after StartDate, until 1PM)
        if (date == vacation.StartDate.AddDays(1))
        {
            return AfterPartialType.Until1PM;
        }

        return AfterPartialType.None;
    }

    #endregion

    #region On-Duty Logic

    private DayStatus? GetOnDutyForDay(int userId, DateOnly date, List<OnDutyEvent> onDutyAssignments)
    {
        var onDuty = onDutyAssignments.FirstOrDefault(od => od.UserId == userId && od.Date == date);

        if (onDuty == null)
        {
            return null;
        }

        // Regular On-Duty
        return new DayStatus
        {
            Type = DayStatusType.OnDuty,
            Label = "On-Duty",
            TimeRange = null,
            Metadata = onDuty.Type.ToString(),
            TargetUrl = "/Public/OnDuty"
        };
    }

    #endregion

    #region Shift Logic

    private DayStatus? GetShiftForDay(int userId, DateOnly date, List<ShiftEvent> shifts)
    {
        // HOME assignments are surfaced via GetHomeForDay; exclude them from the
        // plain Shift bucket so the chip doesn't render as a generic shift.
        var userShifts = shifts
            .Where(s => s.UserId == userId && s.Date == date && !s.IsHome)
            .ToList();

        if (!userShifts.Any())
        {
            return null;
        }

        // If single shift, show its name
        if (userShifts.Count == 1)
        {
            var shift = userShifts[0];
            return new DayStatus
            {
                Type = DayStatusType.Shift,
                Label = shift.NameEn ?? shift.ShiftTypeName,
                TimeRange = $"{shift.Start:HH:mm} - {shift.End:HH:mm}",
                Metadata = shift.ShiftTypeKey,
                TargetUrl = "/Calendar/Table"
            };
        }

        // Multiple shifts: show "Shift" label
        return new DayStatus
        {
            Type = DayStatusType.Shift,
            Label = "Shift",
            TimeRange = null, // Too complex to show
            Metadata = $"{userShifts.Count} shifts",
            TargetUrl = "/Calendar/Table"
        };
    }

    /// <summary>
    /// Task 24: Surfaces HOME shifts on MyTeam as a unified "Home" status.
    /// HomeSourceType lets the JS pick the source icon (rotation / vacation / after).
    /// </summary>
    private DayStatus? GetHomeForDay(int userId, DateOnly date, List<ShiftEvent> shifts)
    {
        var homeShift = shifts
            .Where(s => s.UserId == userId && s.Date == date && s.IsHome)
            .OrderBy(s => s.Start)
            .FirstOrDefault();

        if (homeShift == null)
        {
            return null;
        }

        // SourceTimeOffRequestType: null = rotation, 0 = vacation, 1 = after.
        // Encode in Metadata so the JS layer (myteam.js) can pick the source icon
        // without changing the DayStatus shape.
        var sourceTag = homeShift.SourceTimeOffRequestType switch
        {
            0 => "vacation",
            1 => "after",
            _ => "rotation"
        };

        return new DayStatus
        {
            Type = DayStatusType.Home,
            Label = "Home",
            TimeRange = $"{homeShift.Start:HH:mm} - {homeShift.End:HH:mm}",
            Metadata = sourceTag,
            TargetUrl = "/Calendar/Table"
        };
    }

    #endregion

    #region Chore Logic

    private DayStatus? GetChoreForDay(int userId, DateOnly date, List<ChoreEvent> chores)
    {
        var userChores = chores
            .Where(c => c.UserId == userId && c.Date == date)
            .ToList();

        if (!userChores.Any())
        {
            return null;
        }

        // If single chore, show its name
        if (userChores.Count == 1)
        {
            var chore = userChores[0];
            return new DayStatus
            {
                Type = DayStatusType.Chore,
                Label = chore.Name,
                TimeRange = null,
                Metadata = chore.Id.ToString(),
                TargetUrl = "/Public/Chores"
            };
        }

        // Multiple chores: show count
        return new DayStatus
        {
            Type = DayStatusType.Chore,
            Label = $"Chore +{userChores.Count - 1}",
            TimeRange = null,
            Metadata = $"{userChores.Count} chores",
            TargetUrl = "/Public/Chores"
        };
    }

    #endregion

    #region Data Fetching

    private async Task<List<VacationEvent>> GetVacationsAsync(
        List<int> memberUserIds,
        DateOnly weekStart,
        DateOnly weekEnd)
    {
        // Extend range by 1 day to catch vacation/After extensions
        var extendedEnd = weekEnd.AddDays(1);

        return await _context.TimeOffRequests
            .Where(t =>
                memberUserIds.Contains(t.UserId) &&
                t.Status == RequestStatus.Approved &&
                t.StartDate <= extendedEnd &&
                t.EndDate >= weekStart) // Overlaps with week (accounting for extension)
            .Select(t => new VacationEvent
            {
                UserId = t.UserId,
                StartDate = t.StartDate,
                EndDate = t.EndDate,
                Type = t.Type,
                Label = t.Label
            })
            .ToListAsync();
    }

    private async Task<List<ShiftEvent>> GetShiftsAsync(
        List<int> memberUserIds,
        DateOnly weekStart,
        DateOnly weekEnd)
    {
        // Task 24: Include SourceTimeOffRequest so HOME chip can resolve source
        // (rotation/vacation/after) for the source icon.
        var assignments = await _context.ShiftAssignments
            .Include(a => a.ShiftInstance)
            .ThenInclude(si => si.ShiftType)
            .Include(a => a.SourceTimeOffRequest)
            .Where(a =>
                a.UserId != null &&
                memberUserIds.Contains(a.UserId.Value) &&
                a.ShiftInstance.WorkDate >= weekStart &&
                a.ShiftInstance.WorkDate <= weekEnd)
            .ToListAsync();

        var culture = CultureInfo.CurrentUICulture.Name;
        var result = new List<ShiftEvent>(assignments.Count);
        foreach (var a in assignments)
        {
            result.Add(new ShiftEvent
            {
                UserId = a.UserId!.Value,
                Date = a.ShiftInstance.WorkDate,
                ShiftTypeKey = a.ShiftInstance.ShiftType.Key,
                ShiftTypeName = await _localizationService.ResolveShiftTypeNameAsync(a.ShiftInstance.ShiftType, a.CompanyId, culture),
                NameEn = a.ShiftInstance.ShiftType.NameEn,
                Start = a.ShiftInstance.ShiftType.Start,
                End = a.ShiftInstance.ShiftType.End,
                // Task 24 — HOME unification fields
                IsHome = a.ShiftInstance.ShiftType.IsHome,
                SourceTimeOffRequestType = a.SourceTimeOffRequest != null
                    ? (int?)a.SourceTimeOffRequest.Type
                    : null
            });
        }
        return result;
    }

    private async Task<List<ChoreEvent>> GetChoresAsync(
        List<int> memberUserIds,
        DateOnly weekStart,
        DateOnly weekEnd)
    {
        return await _context.Chores
            .Where(c =>
                memberUserIds.Contains(c.UserId) &&
                c.Date >= weekStart &&
                c.Date <= weekEnd &&
                c.CanceledAt == null)
            .Select(c => new ChoreEvent
            {
                Id = c.Id,
                UserId = c.UserId,
                Date = c.Date,
                Name = c.Title
            })
            .ToListAsync();
    }

    private async Task<List<OnDutyEvent>> GetOnDutyAsync(
        List<int> memberUserIds,
        DateOnly weekStart,
        DateOnly weekEnd)
    {
        return await _context.OnDuties
            .Where(od =>
                memberUserIds.Contains(od.UserId) &&
                od.Date >= weekStart &&
                od.Date <= weekEnd &&
                od.CanceledAt == null)
            .Select(od => new OnDutyEvent
            {
                UserId = od.UserId,
                Date = od.Date,
                Type = od.Type
            })
            .ToListAsync();
    }

    #endregion
}

#region Event DTOs

public class VacationEvent
{
    public int UserId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public TimeOffType Type { get; set; }
    public string? Label { get; set; } // free-text location for DayAt requests (Issue 4)
}

public class ShiftEvent
{
    public int UserId { get; set; }
    public DateOnly Date { get; set; }
    public string ShiftTypeKey { get; set; } = string.Empty;
    public string ShiftTypeName { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }
    /// <summary>True for KEY_HOME / KEY_HOME_PM / KEY_HOME_AM (Task 24).</summary>
    public bool IsHome { get; set; }
    /// <summary>0=Vacation, 1=After, null=rotation. Drives MyTeam HOME source icon.</summary>
    public int? SourceTimeOffRequestType { get; set; }
}

public class ChoreEvent
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateOnly Date { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class OnDutyEvent
{
    public int UserId { get; set; }
    public DateOnly Date { get; set; }
    public OnDutyType Type { get; set; }
}

#endregion

#region Status Result

public enum DayStatusType
{
    Free,
    Vacation,
    VacationPartial,
    AfterPartial,
    OnDuty,
    Shift,
    Chore,
    /// <summary>HOME shift (rotation/vacation/after). Source carried in DayStatus.Metadata. Task 24.</summary>
    Home
}

public class DayStatus
{
    public DayStatusType Type { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? TimeRange { get; set; }
    public string? Metadata { get; set; }
    public string? TargetUrl { get; set; }
}

#endregion
