using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.ViewComponents;

namespace ShiftManager.Services;

/// <summary>
/// Implements <see cref="IOverviewCalendarBuilder"/>. Extracted verbatim (Task #9.2) from
/// <c>OverviewModel</c>'s former private <c>BuildOverviewCalendarAsync</c> +
/// <c>LoadVacationsAsync</c>/<c>LoadShiftsAsync</c>/<c>LoadChoresAsync</c>/<c>LoadOnDutiesAsync</c>/
/// <c>BuildCellsForUser</c> pipeline, parameterized by (companyId, users, range, viewMode,
/// canEditNotes) instead of reading page (<c>this.</c>) state. Behavior is byte-for-byte
/// equivalent to the original: same queries, same HOME/vacation/chore/on-duty/notes aggregation,
/// same output shape (CalendarType="overview", RowMode="Shifts", RowOrderContextKey
/// "overview:{companyId}", IsReadOnly=!canEditNotes).
///
/// One intentional simplification: the original <c>LoadShiftsAsync</c> re-resolved
/// <c>_tenantResolver.GetCurrentTenantId()</c> internally even though the page had already
/// resolved and cached the identical value as <c>CompanyId</c> earlier in the same request (no
/// intervening <c>SetCurrentTenantId</c> call). Since the caller now passes that same value in
/// as the companyId parameter of <see cref="BuildAsync"/>, this service reuses the parameter
/// instead of taking an <c>ITenantResolver</c> dependency and re-deriving it — same value, one
/// fewer dependency.
/// </summary>
public class OverviewCalendarBuilder : IOverviewCalendarBuilder
{
    private readonly AppDbContext _db;
    private readonly ICalendarTextEntryService _textEntryService;
    private readonly ICompanyLocalizationService _companyLocalizationService;
    private readonly IStringLocalizer<SharedResources> _localizer;

    public OverviewCalendarBuilder(
        AppDbContext db,
        ICalendarTextEntryService textEntryService,
        ICompanyLocalizationService companyLocalizationService,
        IStringLocalizer<SharedResources> localizer)
    {
        _db = db;
        _textEntryService = textEntryService;
        _companyLocalizationService = companyLocalizationService;
        _localizer = localizer;
    }

    public async Task<ExcelCalendarTableViewModel> BuildAsync(
        int companyId,
        IReadOnlyList<AppUser> users,
        DateOnly startDate,
        DateOnly endDate,
        string viewMode,
        bool canEditNotes)
    {
        // Load all aggregated data for the date range
        var vacations = await LoadVacationsAsync(users, startDate, endDate);
        var shifts = await LoadShiftsAsync(users, startDate, endDate, companyId);
        var chores = await LoadChoresAsync(users, startDate, endDate);
        var onDuties = await LoadOnDutiesAsync(users, startDate, endDate);
        var notes = await _textEntryService.GetOverviewNotesForCompanyAsync(companyId, startDate, endDate);

        // Load quick-entry text entries for cross-visibility (📝 badge on Overview)
        var userIds = users.Select(u => u.Id);
        var textEntriesWithType = await _textEntryService.GetForUsersAndDateRangeWithTypeAsync(userIds, startDate, endDate);
        // Filter to QuickEntry only (OverviewNotes are already in 'notes' dict)
        var quickEntries = textEntriesWithType.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value
                .Where(e => e.EntryType == CalendarTextEntryType.QuickEntry)
                .Select(e => e.Text)
                .ToList())
            .Where(kvp => kvp.Value.Count > 0)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        // Build rows - one per user
        var rows = new List<ExcelCalendarRow>();
        foreach (var user in users)
        {
            var row = new ExcelCalendarRow
            {
                Id = $"user-{user.Id}",
                Label = user.DisplayName
            };

            // Build cells for each date
            row.Cells = BuildCellsForUser(user.Id, startDate, endDate, vacations, shifts, chores, onDuties, notes, quickEntries);
            rows.Add(row);
        }

        var calendarData = new ExcelCalendarTableViewModel
        {
            StartDate = startDate,
            EndDate = endDate,
            ViewMode = viewMode,
            // Issue 7: Overview is no longer hard-locked to view-only. It stays read-only for
            // ASSIGNMENTS (assignment slash-menu is gated by data-can-assign / CanEdit elsewhere and
            // never enabled here), but note / free-text editing is opened to anyone who holds
            // WriteOverviewNotes — Molecule Admins, קב"ר, and (Issue 3) every molecule member. Driving
            // IsReadOnly from the same note-edit grant removes the misleading "view only" banner and
            // makes the cells interactive for those roles instead of inert.
            IsReadOnly = !canEditNotes,
            CalendarType = "overview",
            Rows = rows
        };
        calendarData.RowMode = "Shifts";
        // +1 for the <thead> column-header row (ARIA 1.2 §6.6.4).
        calendarData.TotalRows = calendarData.Rows.Count + (calendarData.Groups?.Count ?? 0) + 1;
        calendarData.RowOrderContextKey = $"overview:{companyId}";

        return calendarData;
    }

    private async Task<Dictionary<(int UserId, DateOnly Date), (bool HasVacation, string? DayAtLabel)>> LoadVacationsAsync(
        IReadOnlyList<AppUser> users, DateOnly startDate, DateOnly endDate)
    {
        // Get approved time-off requests for users in date range
        var userIds = users.Select(u => u.Id).ToList();

        var timeOffRequests = await _db.TimeOffRequests
            .Where(t => userIds.Contains(t.UserId) &&
                        t.StartDate <= endDate &&
                        t.EndDate >= startDate &&
                        t.Status == RequestStatus.Approved)
            .ToListAsync();

        // Expand time-off requests to per-day records. Vacation/After → HasVacation (palm-tree badge);
        // "Day at [X]" (DayAt) → its own DayAtLabel so it renders as "יום {Label}", never the vacation
        // symbol (Issue: day-X showed as vacation). Independent flags so a day can carry both if needed.
        var result = new Dictionary<(int UserId, DateOnly Date), (bool HasVacation, string? DayAtLabel)>();
        foreach (var timeOff in timeOffRequests)
        {
            for (var date = timeOff.StartDate; date <= timeOff.EndDate; date = date.AddDays(1))
            {
                if (date >= startDate && date <= endDate)
                {
                    result.TryGetValue((timeOff.UserId, date), out var existing);
                    if (timeOff.Type == TimeOffType.DayAt)
                        existing.DayAtLabel = timeOff.Label;
                    else
                        existing.HasVacation = true;
                    result[(timeOff.UserId, date)] = existing;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Per-user-per-date shift item used to render the Overview cell.
    /// Carries HOME-specific fields so the shared _CalendarRow partial can
    /// render HOME chips with source/house icons + time range (Task 23).
    /// </summary>
    private record OverviewShiftItem(
        string Name,
        bool IsHome,
        string? ShiftStart,
        string? ShiftEnd,
        int? SourceTimeOffRequestId,
        int? SourceTimeOffRequestType);

    private async Task<Dictionary<(int UserId, DateOnly Date), List<OverviewShiftItem>>> LoadShiftsAsync(
        IReadOnlyList<AppUser> users, DateOnly startDate, DateOnly endDate, int companyId)
    {
        var userIds = users.Select(u => u.Id).ToList();

        // Include SourceTimeOffRequest so the projection can expose its Type for HOME chip
        // source-icon resolution (rotation/vacation/after) — Task 23.
        var assignments = await _db.ShiftAssignments
            .Include(sa => sa.ShiftInstance)
                .ThenInclude(si => si.ShiftType)
            .Include(sa => sa.SourceTimeOffRequest)
            .Where(sa => ((sa.UserId.HasValue && userIds.Contains(sa.UserId.Value)) ||
                         (sa.TraineeUserId.HasValue && userIds.Contains(sa.TraineeUserId.Value))) &&
                        sa.ShiftInstance.WorkDate >= startDate &&
                        sa.ShiftInstance.WorkDate <= endDate)
            .ToListAsync();

        var culture = System.Globalization.CultureInfo.CurrentUICulture.Name;

        var result = new Dictionary<(int UserId, DateOnly Date), List<OverviewShiftItem>>();
        foreach (var assignment in assignments)
        {
            var date = assignment.ShiftInstance.WorkDate;
            var shiftType = assignment.ShiftInstance.ShiftType;
            var shiftName = shiftType != null
                ? await _companyLocalizationService.ResolveShiftTypeNameAsync(
                    shiftType, companyId, culture)
                : _localizer["Shift"].Value;

            // ShiftType.IsHome is [NotMapped] — safe here because the projection runs
            // client-side after .ToListAsync(). Same pattern as Calendar/Shifts.
            var isHome = shiftType?.IsHome == true;
            var shiftStart = shiftType?.Start.ToString("HH:mm");
            var shiftEnd = shiftType?.End.ToString("HH:mm");
            var sourceId = assignment.SourceTimeOffRequestId;
            var sourceType = assignment.SourceTimeOffRequest != null
                ? (int?)assignment.SourceTimeOffRequest.Type
                : null;

            // Add for primary user if assigned
            if (assignment.UserId.HasValue && userIds.Contains(assignment.UserId.Value))
            {
                var key = (assignment.UserId.Value, date);
                if (!result.ContainsKey(key))
                {
                    result[key] = new List<OverviewShiftItem>();
                }
                result[key].Add(new OverviewShiftItem(shiftName, isHome, shiftStart, shiftEnd, sourceId, sourceType));
            }

            // Also add for trainee if applicable
            if (assignment.TraineeUserId.HasValue && userIds.Contains(assignment.TraineeUserId.Value))
            {
                var traineeKey = (assignment.TraineeUserId.Value, date);
                if (!result.ContainsKey(traineeKey))
                {
                    result[traineeKey] = new List<OverviewShiftItem>();
                }
                result[traineeKey].Add(new OverviewShiftItem(
                    $"{shiftName} ({_localizer["Trainee"].Value})",
                    isHome, shiftStart, shiftEnd, sourceId, sourceType));
            }
        }

        return result;
    }

    private async Task<Dictionary<(int UserId, DateOnly Date), List<string>>> LoadChoresAsync(
        IReadOnlyList<AppUser> users, DateOnly startDate, DateOnly endDate)
    {
        var userIds = users.Select(u => u.Id).ToList();

        var chores = await _db.Chores
            .Include(c => c.ChoreType)
            .Where(c => userIds.Contains(c.UserId) &&
                        c.Date >= startDate &&
                        c.Date <= endDate &&
                        c.CanceledAt == null)
            .ToListAsync();

        var isHebrew = System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("he");
        var result = new Dictionary<(int UserId, DateOnly Date), List<string>>();
        foreach (var chore in chores)
        {
            var key = (chore.UserId, chore.Date);
            if (!result.ContainsKey(key))
            {
                result[key] = new List<string>();
            }
            var choreName = chore.ChoreType != null
                ? (isHebrew && !string.IsNullOrWhiteSpace(chore.ChoreType.NameHe) ? chore.ChoreType.NameHe : chore.ChoreType.NameEn ?? chore.ChoreType.DisplayName)
                : chore.Title;
            result[key].Add(choreName);
        }

        return result;
    }

    private async Task<Dictionary<(int UserId, DateOnly Date), List<string>>> LoadOnDutiesAsync(
        IReadOnlyList<AppUser> users, DateOnly startDate, DateOnly endDate)
    {
        var userIds = users.Select(u => u.Id).ToList();

        var onDuties = await _db.OnDuties
            .Where(od => userIds.Contains(od.UserId) &&
                        od.Date >= startDate &&
                        od.Date <= endDate &&
                        od.CanceledAt == null)
            .ToListAsync();

        var result = new Dictionary<(int UserId, DateOnly Date), List<string>>();
        foreach (var onDuty in onDuties)
        {
            var key = (onDuty.UserId, onDuty.Date);
            if (!result.ContainsKey(key))
            {
                result[key] = new List<string>();
            }
            result[key].Add(onDuty.Type.ToString());
        }

        return result;
    }

    private Dictionary<DateOnly, ExcelCalendarCell> BuildCellsForUser(
        int userId,
        DateOnly startDate,
        DateOnly endDate,
        Dictionary<(int UserId, DateOnly Date), (bool HasVacation, string? DayAtLabel)> vacations,
        Dictionary<(int UserId, DateOnly Date), List<OverviewShiftItem>> shifts,
        Dictionary<(int UserId, DateOnly Date), List<string>> chores,
        Dictionary<(int UserId, DateOnly Date), List<string>> onDuties,
        Dictionary<(int UserId, DateOnly Date), string> notes,
        Dictionary<(int UserId, DateOnly Date), List<string>> quickEntries)
    {
        var cells = new Dictionary<DateOnly, ExcelCalendarCell>();

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            var cell = new ExcelCalendarCell();
            var key = (userId, date);
            var assignments = new List<ExcelCalendarAssignment>();

            // Add shifts as assignments. HOME shifts populate IsHome + source/time fields
            // so the shared _CalendarRow partial renders the unified HOME chip (Task 23).
            if (shifts.TryGetValue(key, out var shiftList))
            {
                foreach (var shift in shiftList)
                {
                    assignments.Add(new ExcelCalendarAssignment
                    {
                        Id = 0, // Not editable
                        Name = shift.Name,
                        Role = "shift",
                        UserId = userId,
                        IsHome = shift.IsHome,
                        ShiftStart = shift.ShiftStart,
                        ShiftEnd = shift.ShiftEnd,
                        SourceTimeOffRequestId = shift.SourceTimeOffRequestId,
                        SourceTimeOffRequestType = shift.SourceTimeOffRequestType
                    });
                }
            }

            // Add chores as assignments
            if (chores.TryGetValue(key, out var choreList))
            {
                foreach (var chore in choreList)
                {
                    assignments.Add(new ExcelCalendarAssignment
                    {
                        Id = 0,
                        Name = chore,
                        Role = "chore",
                        UserId = userId
                    });
                }
            }

            // Add on-duties as assignments
            if (onDuties.TryGetValue(key, out var dutyList))
            {
                foreach (var duty in dutyList)
                {
                    assignments.Add(new ExcelCalendarAssignment
                    {
                        Id = 0,
                        Name = duty,
                        Role = "duty",
                        UserId = userId
                    });
                }
            }

            cell.Assignments = assignments;

            // Add overlay data
            vacations.TryGetValue(key, out var timeOff);
            var hasVacationFlag = timeOff.HasVacation;
            var dayAtLabel = timeOff.DayAtLabel;
            var hasTextEntries = quickEntries.TryGetValue(key, out var entryTexts) && entryTexts.Count > 0;

            if (hasVacationFlag || dayAtLabel != null || hasTextEntries)
            {
                cell.Overlay = new ExcelCalendarOverlay
                {
                    HasVacation = hasVacationFlag,
                    DayAtLabel = dayAtLabel
                };

                // Cross-visibility: show QuickEntry text entries from Shifts/Chores/OnCall as 📝 badge
                if (hasTextEntries)
                {
                    cell.Overlay.HasTextEntry = true;
                    cell.Overlay.TextEntryTexts = entryTexts!;
                }
            }

            // Add overview note (renders as plain text in cell)
            if (notes.TryGetValue(key, out var note))
            {
                cell.Note = note;
            }

            cells[date] = cell;
        }

        return cells;
    }
}
