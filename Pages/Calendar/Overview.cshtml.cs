using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Resources;
using ShiftManager.Hubs;
using ShiftManager.Services;
using ShiftManager.ViewComponents;
using ShiftManager.Models.Support;
using System.Security.Claims;

namespace ShiftManager.Pages.Calendar;

/// <summary>
/// Excel-style Overview Calendar page - View-only aggregation of all user data.
/// Company-scoped: shows users in current company as rows, days as columns.
/// Each cell shows aggregated status (vacations, shifts, chores, on-duty) plus notes.
/// </summary>
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires [Authorize];
// users filtered by CompanyId; note save validates user is in same company
[Authorize]
public class OverviewModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ICalendarTextEntryService _textEntryService;
    private readonly IGrantService _grantService;
    private readonly ICompanyContext _companyContext;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly ICompanyLocalizationService _companyLocalizationService;
    private readonly ITenantResolver _tenantResolver;
    private readonly IAuditLogService _auditLogService;
    private readonly ICalendarNotificationService _notificationService;
    private readonly ILogger<OverviewModel> _logger;

    public OverviewModel(
        AppDbContext db,
        ICalendarTextEntryService textEntryService,
        IGrantService grantService,
        ICompanyContext companyContext,
        IStringLocalizer<SharedResources> localizer,
        ICompanyLocalizationService companyLocalizationService,
        ITenantResolver tenantResolver,
        IAuditLogService auditLogService,
        ICalendarNotificationService notificationService,
        ILogger<OverviewModel> logger)
    {
        _db = db;
        _textEntryService = textEntryService;
        _grantService = grantService;
        _companyContext = companyContext;
        _localizer = localizer;
        _companyLocalizationService = companyLocalizationService;
        _tenantResolver = tenantResolver;
        _auditLogService = auditLogService;
        _notificationService = notificationService;
        _logger = logger;
    }

    // Query parameters
    [BindProperty(SupportsGet = true)]
    public string? Start { get; set; }

    [BindProperty(SupportsGet = true)]
    public string ViewMode { get; set; } = "week"; // week, 2weeks, month

    [BindProperty(SupportsGet = true)]
    public string? UsersFilter { get; set; } // all, active, inactive, or specific group

    [BindProperty(SupportsGet = true)]
    public bool JustMine { get; set; }

    // Page properties
    public ExcelCalendarTableViewModel CalendarData { get; set; } = new();
    public bool CanEditNotes { get; set; }
    public List<AppUser> Users { get; set; } = new();
    public int CurrentUserId { get; set; }
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;

    // Navigation
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string PreviousStart { get; set; } = string.Empty;
    public string NextStart { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync()
    {
        // Get current user ID
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
        {
            _logger.LogError("Invalid or missing NameIdentifier claim");
            return RedirectToPage("/Error");
        }
        CurrentUserId = currentUserId;

        // Honor the active-company switcher (member_selected_company cookie) for multi-company
        // users — the same source the Shifts calendar uses via _tenantResolver. _companyContext.CompanyId
        // only reads the login-time claim, which pinned Overview to the user's primary company and made
        // the context switcher appear to do nothing here.
        var companyId = _tenantResolver.GetCurrentTenantId();
        if (companyId <= 0)
        {
            _logger.LogWarning("User {UserId} has no company context", currentUserId);
            return RedirectToPage("/Error");
        }
        CompanyId = companyId;

        // Load company details
        var company = await _db.Companies
            .FirstOrDefaultAsync(c => c.Id == companyId);

        if (company == null)
        {
            _logger.LogWarning("Company {CompanyId} not found", companyId);
            return RedirectToPage("/Error");
        }
        CompanyName = company.LocalizedName;

        // Calculate date range
        CalculateDateRange();

        // Check note editing permission
        CanEditNotes = await _grantService.HasGrantAsync(currentUserId, "WriteOverviewNotes");

        // Load users in this company
        await LoadUsersAsync();

        // Build calendar data
        await BuildOverviewCalendarAsync();

        _logger.LogInformation(
            "Overview calendar loaded for User {UserId}, Company {CompanyId}, ViewMode {ViewMode}",
            currentUserId, CompanyId, ViewMode);

        return Page();
    }

    private void CalculateDateRange()
    {
        // Parse start date or default to start of current week
        var today = DateOnly.FromDateTime(DateTime.Today);

        // "Next 7 days" is anchored to TODAY (today..today+6), ignoring Start; nav is inert.
        if (ViewMode == "next7")
        {
            StartDate = today;
            EndDate = today.AddDays(6);
            PreviousStart = today.ToString("yyyy-MM-dd");
            NextStart = today.ToString("yyyy-MM-dd");
            return;
        }

        if (!string.IsNullOrEmpty(Start) && DateOnly.TryParse(Start, out var parsedDate))
        {
            StartDate = parsedDate;
        }
        else
        {
            // Default to Sunday of current week
            StartDate = GetStartOfWeek(today);
        }

        // Calculate end date based on view mode
        EndDate = ViewMode switch
        {
            "2weeks" => StartDate.AddDays(13),
            "month" => StartDate.AddDays(DateTime.DaysInMonth(StartDate.Year, StartDate.Month) - 1),
            _ => StartDate.AddDays(6) // week
        };

        // Calculate navigation dates
        var daysToMove = ViewMode switch
        {
            "2weeks" => 14,
            "month" => DateTime.DaysInMonth(StartDate.Year, StartDate.Month),
            _ => 7
        };

        PreviousStart = StartDate.AddDays(-daysToMove).ToString("yyyy-MM-dd");
        NextStart = StartDate.AddDays(daysToMove).ToString("yyyy-MM-dd");
    }

    private static DateOnly GetStartOfWeek(DateOnly date)
    {
        int daysFromSunday = (int)date.DayOfWeek;
        return date.AddDays(-daysFromSunday);
    }

    private async Task LoadUsersAsync()
    {
        var query = _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.CompanyId == CompanyId);

        // Exclude Mil + GroupUser — only Standard accounts are visible on Overview
        query = query.Where(u => u.AccountType == AccountType.Standard);

        // Apply user filter
        query = UsersFilter switch
        {
            "inactive" => query.Where(u => !u.IsActive),
            "active" or null or "" => query.Where(u => u.IsActive), // Default to active users
            _ => query.Where(u => u.IsActive) // Default for unknown values
        };

        Users = await query
            .OrderBy(u => u.DisplayName)
            .ToListAsync();

        // If "Just Mine" is enabled, filter to just current user
        if (JustMine)
        {
            Users = Users.Where(u => u.Id == CurrentUserId).ToList();
        }
    }

    private async Task BuildOverviewCalendarAsync()
    {
        // Load all aggregated data for the date range
        var vacations = await LoadVacationsAsync();
        var shifts = await LoadShiftsAsync();
        var chores = await LoadChoresAsync();
        var onDuties = await LoadOnDutiesAsync();
        var notes = await _textEntryService.GetOverviewNotesForCompanyAsync(CompanyId, StartDate, EndDate);

        // Load quick-entry text entries for cross-visibility (📝 badge on Overview)
        var userIds = Users.Select(u => u.Id);
        var textEntriesWithType = await _textEntryService.GetForUsersAndDateRangeWithTypeAsync(userIds, StartDate, EndDate);
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
        foreach (var user in Users)
        {
            var row = new ExcelCalendarRow
            {
                Id = $"user-{user.Id}",
                Label = user.DisplayName
            };

            // Build cells for each date
            row.Cells = BuildCellsForUser(user.Id, vacations, shifts, chores, onDuties, notes, quickEntries);
            rows.Add(row);
        }

        CalendarData = new ExcelCalendarTableViewModel
        {
            StartDate = StartDate,
            EndDate = EndDate,
            ViewMode = ViewMode,
            // Issue 7: Overview is no longer hard-locked to view-only. It stays read-only for
            // ASSIGNMENTS (assignment slash-menu is gated by data-can-assign / CanEdit elsewhere and
            // never enabled here), but note / free-text editing is opened to anyone who holds
            // WriteOverviewNotes — Molecule Admins, קב"ר, and (Issue 3) every molecule member. Driving
            // IsReadOnly from the same note-edit grant removes the misleading "view only" banner and
            // makes the cells interactive for those roles instead of inert.
            IsReadOnly = !CanEditNotes,
            CalendarType = "overview",
            Rows = rows
        };
        CalendarData.RowMode = "Shifts";
        // +1 for the <thead> column-header row (ARIA 1.2 §6.6.4).
        CalendarData.TotalRows = CalendarData.Rows.Count + (CalendarData.Groups?.Count ?? 0) + 1;
        CalendarData.RowOrderContextKey = $"overview:{CompanyId}";
    }

    private async Task<Dictionary<(int UserId, DateOnly Date), (bool HasVacation, string? DayAtLabel)>> LoadVacationsAsync()
    {
        // Get approved time-off requests for users in date range
        var userIds = Users.Select(u => u.Id).ToList();

        var timeOffRequests = await _db.TimeOffRequests
            .Where(t => userIds.Contains(t.UserId) &&
                        t.StartDate <= EndDate &&
                        t.EndDate >= StartDate &&
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
                if (date >= StartDate && date <= EndDate)
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

    private async Task<Dictionary<(int UserId, DateOnly Date), List<OverviewShiftItem>>> LoadShiftsAsync()
    {
        var userIds = Users.Select(u => u.Id).ToList();

        // Include SourceTimeOffRequest so the projection can expose its Type for HOME chip
        // source-icon resolution (rotation/vacation/after) — Task 23.
        var assignments = await _db.ShiftAssignments
            .Include(sa => sa.ShiftInstance)
                .ThenInclude(si => si.ShiftType)
            .Include(sa => sa.SourceTimeOffRequest)
            .Where(sa => ((sa.UserId.HasValue && userIds.Contains(sa.UserId.Value)) ||
                         (sa.TraineeUserId.HasValue && userIds.Contains(sa.TraineeUserId.Value))) &&
                        sa.ShiftInstance.WorkDate >= StartDate &&
                        sa.ShiftInstance.WorkDate <= EndDate)
            .ToListAsync();

        var companyId = _tenantResolver.GetCurrentTenantId();
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

    private async Task<Dictionary<(int UserId, DateOnly Date), List<string>>> LoadChoresAsync()
    {
        var userIds = Users.Select(u => u.Id).ToList();

        var chores = await _db.Chores
            .Include(c => c.ChoreType)
            .Where(c => userIds.Contains(c.UserId) &&
                        c.Date >= StartDate &&
                        c.Date <= EndDate &&
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

    private async Task<Dictionary<(int UserId, DateOnly Date), List<string>>> LoadOnDutiesAsync()
    {
        var userIds = Users.Select(u => u.Id).ToList();

        var onDuties = await _db.OnDuties
            .Where(od => userIds.Contains(od.UserId) &&
                        od.Date >= StartDate &&
                        od.Date <= EndDate &&
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
        Dictionary<(int UserId, DateOnly Date), (bool HasVacation, string? DayAtLabel)> vacations,
        Dictionary<(int UserId, DateOnly Date), List<OverviewShiftItem>> shifts,
        Dictionary<(int UserId, DateOnly Date), List<string>> chores,
        Dictionary<(int UserId, DateOnly Date), List<string>> onDuties,
        Dictionary<(int UserId, DateOnly Date), string> notes,
        Dictionary<(int UserId, DateOnly Date), List<string>> quickEntries)
    {
        var cells = new Dictionary<DateOnly, ExcelCalendarCell>();

        for (var date = StartDate; date <= EndDate; date = date.AddDays(1))
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

    // API endpoint for saving notes (AJAX)
    public async Task<IActionResult> OnPostSaveNoteAsync([FromBody] SaveNoteRequest request)
    {
        // Validate user has permission
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
        {
            return new JsonResult(new { success = false, error = "Unauthorized" }) { StatusCode = 401 };
        }

        var canEdit = await _grantService.HasGrantAsync(currentUserId, "WriteOverviewNotes");
        if (!canEdit)
        {
            return new JsonResult(new { success = false, error = "Forbidden" }) { StatusCode = 403 };
        }

        // Validate company context against the ACTIVE company (switcher-aware), so a note writes
        // to the company the user is currently viewing on Overview — not their login-time claim.
        var resolvedCompanyId = _tenantResolver.GetCurrentTenantId();
        int? companyId = resolvedCompanyId > 0 ? resolvedCompanyId : (int?)null;
        if (!companyId.HasValue)
        {
            return new JsonResult(new { success = false, error = "No company context" }) { StatusCode = 400 };
        }

        // Validate target user is in the same company
        var targetUser = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == request.UserId);
        if (targetUser == null || targetUser.CompanyId != companyId.Value)
        {
            return new JsonResult(new { success = false, error = "User not found in company" }) { StatusCode = 400 };
        }

        try
        {
            if (string.IsNullOrWhiteSpace(request.Note))
            {
                // Delete note if empty
                await _textEntryService.DeleteOverviewNoteAsync(request.UserId, request.Date, companyId.Value);

                await _auditLogService.LogAsync(
                    action: "OverviewNoteDeleted",
                    entityType: "CalendarTextEntry",
                    entityId: 0,
                    description: $"Deleted overview note for user {request.UserId} on {request.Date:yyyy-MM-dd}");

                // Notify connected clients
                await _notificationService.NotifyNoteChangedAsync(
                    CalendarGroups.Overview(companyId.Value),
                    new CalendarNoteChangedEvent(request.UserId, request.Date, null, "deleted"));
            }
            else
            {
                if (request.Note.Length > 500)
                {
                    return new JsonResult(new { success = false, error = "Note must not exceed 500 characters" }) { StatusCode = 400 };
                }

                // Save note (upsert — creates or updates the single OverviewNote for this user/date/company)
                var saved = await _textEntryService.SetOverviewNoteAsync(request.UserId, request.Date, companyId.Value, request.Note.Trim(), currentUserId);

                var changeType = saved.UpdatedAt.HasValue ? "updated" : "created";
                await _auditLogService.LogAsync(
                    action: $"OverviewNote{(changeType == "created" ? "Created" : "Updated")}",
                    entityType: "CalendarTextEntry",
                    entityId: saved.Id,
                    description: $"{changeType} overview note for user {request.UserId} on {request.Date:yyyy-MM-dd}: '{request.Note.Trim()}'");

                // Notify connected clients
                await _notificationService.NotifyNoteChangedAsync(
                    CalendarGroups.Overview(companyId.Value),
                    new CalendarNoteChangedEvent(request.UserId, request.Date, request.Note.Trim(), changeType));
            }

            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving note for user {UserId} on {Date}", request.UserId, request.Date);
            return new JsonResult(new { success = false, error = "Failed to save note" }) { StatusCode = 500 };
        }
    }

    public class SaveNoteRequest
    {
        public int UserId { get; set; }
        public DateOnly Date { get; set; }
        public string Note { get; set; } = string.Empty;
    }
}
