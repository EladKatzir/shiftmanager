using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Resources;
using ShiftManager.Services;
using ShiftManager.ViewComponents;
using System.Globalization;
using System.Security.Claims;

namespace ShiftManager.Pages.Calendar;

/// <summary>
/// Excel-style Chores Calendar page - Shows chore assignments by user across dates.
/// Chores are molecule-scoped (cross-company within molecule).
/// </summary>
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires [Authorize];
// chores are molecule-scoped (cross-company within molecule); users filtered by companyIds in molecule
[Authorize]
public class ChoresModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IChoreService _choreService;
    private readonly IChoreTypeService _choreTypeService;
    private readonly IGrantService _grantService;
    private readonly ICompanyContext _companyContext;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly ILogger<ChoresModel> _logger;
    private readonly ICalendarTextEntryService _textEntryService;
    private readonly IShiftCalendarService _calendarService;
    private readonly IJusticeService _justiceService;

    public ChoresModel(
        AppDbContext db,
        IChoreService choreService,
        IChoreTypeService choreTypeService,
        IGrantService grantService,
        ICompanyContext companyContext,
        IStringLocalizer<SharedResources> localizer,
        ILogger<ChoresModel> logger,
        ICalendarTextEntryService textEntryService,
        IShiftCalendarService calendarService,
        IJusticeService justiceService)
    {
        _db = db;
        _choreService = choreService;
        _choreTypeService = choreTypeService;
        _grantService = grantService;
        _companyContext = companyContext;
        _localizer = localizer;
        _logger = logger;
        _textEntryService = textEntryService;
        _calendarService = calendarService;
        _justiceService = justiceService;
    }

    // Query parameters
    [BindProperty(SupportsGet = true)]
    public int? MoleculeId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Start { get; set; }

    [BindProperty(SupportsGet = true)]
    public string ViewMode { get; set; } = "week"; // week, 2weeks, month

    [BindProperty(SupportsGet = true)]
    public int? ChoreTypeFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool JustMine { get; set; }

    // Page properties
    public ExcelCalendarTableViewModel CalendarData { get; set; } = new();
    public bool CanEdit { get; set; }
    // Grant NameKeys that would unlock editing when read-only; empty when CanEdit. Drives the "?" help.
    public List<string> RequiredGrantNameKeys { get; set; } = new();
    // See Shifts.cshtml.cs for rationale: quick-entry toggle visible to any user with WriteOverviewNotes.
    public bool CanWriteNote { get; set; }
    public List<Molecule> AvailableMolecules { get; set; } = new();
    public Molecule? SelectedMolecule { get; set; }
    public List<ChoreType> ChoreTypes { get; set; } = new();
    public int CurrentUserId { get; set; }
    public List<AppUser> Users { get; set; } = new();

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

        // Get user's company and molecule
        var companyId = _companyContext.CompanyId;
        if (!companyId.HasValue)
        {
            _logger.LogWarning("User {UserId} has no company context", currentUserId);
            return RedirectToPage("/Error");
        }

        var userCompany = await _db.Companies
            .Include(c => c.Molecule)
            .FirstOrDefaultAsync(c => c.Id == companyId.Value);

        if (userCompany?.MoleculeId == null)
        {
            _logger.LogWarning("User's company {CompanyId} has no molecule", companyId.Value);
            return RedirectToPage("/Error");
        }

        // Set default MoleculeId if not provided
        if (!MoleculeId.HasValue)
        {
            MoleculeId = userCompany.MoleculeId;
        }

        // Load available molecules (user has access to via grants or their own)
        await LoadAvailableMoleculesAsync(currentUserId, userCompany.MoleculeId.Value);

        // Validate selected molecule is accessible
        if (!AvailableMolecules.Any(m => m.Id == MoleculeId))
        {
            MoleculeId = userCompany.MoleculeId;
        }

        SelectedMolecule = AvailableMolecules.FirstOrDefault(m => m.Id == MoleculeId);

        // Load chore types for the selected molecule
        if (MoleculeId.HasValue)
        {
            ChoreTypes = await _choreTypeService.GetChoreTypesForMoleculeAsync(MoleculeId.Value);
        }

        // Calculate date range
        CalculateDateRange();

        // Check edit permission
        CanEdit = await _grantService.HasGrantAsync(currentUserId, "AssignChores");
        CanWriteNote = CanEdit || await _grantService.HasGrantAsync(currentUserId, "WriteOverviewNotes");

        // When read-only, surface the grant the user is missing in the calendar's "?" help button.
        RequiredGrantNameKeys = CanEdit
            ? new List<string>()
            : await _grantService.GetGrantNameKeysAsync("AssignChores");

        // Build calendar data
        if (MoleculeId.HasValue)
        {
            await BuildUserBasedCalendarAsync(MoleculeId.Value);
        }

        _logger.LogInformation(
            "Chores calendar loaded for User {UserId}, Molecule {MoleculeId}, ViewMode {ViewMode}",
            currentUserId, MoleculeId, ViewMode);

        return Page();
    }

    private void CalculateDateRange()
    {
        // Parse start date or default to start of current week
        var today = DateOnly.FromDateTime(DateTime.Today);
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

        PreviousStart = StartDate.AddDays(-daysToMove).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        NextStart = StartDate.AddDays(daysToMove).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static DateOnly GetStartOfWeek(DateOnly date)
    {
        int daysFromSunday = (int)date.DayOfWeek;
        return date.AddDays(-daysFromSunday);
    }

    private async Task LoadAvailableMoleculesAsync(int userId, int userMoleculeId)
    {
        // Use grant-based scope resolution that handles all scope levels
        // (Project → Area → Molecule → Company → Self)
        var moleculeIds = new HashSet<int>(
            await _grantService.GetAccessibleMoleculeIdsForGrantAsync(userId, "ViewChores"));

        // Also include molecules from AssignChores grant
        foreach (var id in await _grantService.GetAccessibleMoleculeIdsForGrantAsync(userId, "AssignChores"))
            moleculeIds.Add(id);

        // Always include user's own molecule as fallback
        moleculeIds.Add(userMoleculeId);

        AvailableMolecules = await _db.Molecules
            .Where(m => moleculeIds.Contains(m.Id) && m.IsActive)
            .OrderBy(m => m.DisplayName)
            .ToListAsync();
    }

    private async Task BuildUserBasedCalendarAsync(int moleculeId)
    {
        // Get all users in companies belonging to this molecule
        var users = await GetUsersForMoleculeAsync(moleculeId);

        // Expose full user list for bottom-sheet dropdown (before JustMine filter)
        Users = users;

        // Filter to just current user if "Just Mine" is enabled
        if (JustMine)
        {
            users = users.Where(u => u.Id == CurrentUserId).ToList();
        }

        // Get chores for the date range and molecule
        var chores = await _choreService.GetChoresAsync(
            startDate: StartDate,
            endDate: EndDate,
            moleculeId: moleculeId,
            includeCancel: false);

        // Apply chore type filter if set
        if (ChoreTypeFilter.HasValue)
        {
            chores = chores.Where(c => c.ChoreTypeId == ChoreTypeFilter.Value).ToList();
        }

        // Get overlays (vacation, on-duty, other shifts) — same pattern as Calendar/Shifts
        var overlays = await _calendarService.GetOverlaysAsync(moleculeId, StartDate, EndDate);

        // Task 24: Load HOME shift assignments for the same molecule. Rendered as
        // read-only chips on the user row so admins can see when a user is unavailable.
        var homeShifts = await LoadHomeShiftsAsync(Users.Select(u => u.Id).ToList());

        // Load text entries + overview notes for all users (cross-company via IgnoreQueryFilters)
        var allUserIds = Users.Select(u => u.Id);
        var allEntriesWithType = await _textEntryService.GetForUsersAndDateRangeWithTypeAsync(allUserIds, StartDate, EndDate);
        var textEntries = allEntriesWithType.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value
                .Where(e => e.EntryType == CalendarTextEntryType.QuickEntry)
                .Select(e => (e.Id, e.Text))
                .ToList())
            .Where(kvp => kvp.Value.Count > 0)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        // SECURITY: Only show overview notes from the viewer's own company (notes are company-scoped)
        var viewerCompanyId = _companyContext.CompanyId ?? 0;
        var overviewNotes = allEntriesWithType.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value
                .Where(e => e.EntryType == CalendarTextEntryType.OverviewNote && e.CompanyId == viewerCompanyId)
                .Select(e => e.Text)
                .FirstOrDefault())
            .Where(kvp => kvp.Value != null)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value!);

        // Build groups by chore type
        var isHebrew = CultureInfo.CurrentUICulture.Name.StartsWith("he", StringComparison.Ordinal);
        var groups = ChoreTypes.Select(ct => new ExcelCalendarGroup
        {
            Id = $"choretype-{ct.Id}",
            Name = LocalizeChoreTypeName(ct, isHebrew),
            SortOrder = ct.SortOrder
        }).ToList();

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
            row.Cells = BuildCellsForUser(user.Id, chores, overlays, textEntries, overviewNotes, homeShifts, isHebrew);
            rows.Add(row);
        }

        CalendarData = new ExcelCalendarTableViewModel
        {
            StartDate = StartDate,
            EndDate = EndDate,
            ViewMode = ViewMode,
            IsReadOnly = !CanEdit,
            CalendarType = "chores",
            Rows = rows,
            Groups = groups.Any() ? groups : null,
            RequiredGrantNameKeys = RequiredGrantNameKeys
        };
        CalendarData.RowMode = "Chores";
        CalendarData.TotalRows = CalendarData.Rows.Count + (CalendarData.Groups?.Count ?? 0);
    }

    private async Task<List<AppUser>> GetUsersForMoleculeAsync(int moleculeId)
    {
        // Get all companies in this molecule
        var companyIds = await _db.Companies
            .Where(c => c.MoleculeId == moleculeId)
            .Select(c => c.Id)
            .ToListAsync();

        // Get active users in those companies
        return await _db.Users
            .IgnoreQueryFilters()
            .Where(u => companyIds.Contains(u.CompanyId) && u.IsActive)
            .OrderBy(u => u.DisplayName)
            .Select(u => new AppUser
            {
                Id = u.Id,
                DisplayName = u.DisplayName,
                CompanyId = u.CompanyId
            })
            .ToListAsync();
    }

    /// <summary>
    /// Per-user-per-date HOME shift item used to render the read-only HOME overlay
    /// chip on Chores/OnCall calendars (Task 24). Carries source-icon + time fields.
    /// </summary>
    private record HomeShiftItem(
        string Name,
        string? ShiftStart,
        string? ShiftEnd,
        int? SourceTimeOffRequestId,
        int? SourceTimeOffRequestType);

    private async Task<Dictionary<(int UserId, DateOnly Date), List<HomeShiftItem>>> LoadHomeShiftsAsync(List<int> userIds)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<(int UserId, DateOnly Date), List<HomeShiftItem>>();
        }

        // SECURITY-AUDITED: SAFE — userIds were already filtered by molecule membership
        // in GetUsersForMoleculeAsync; date range is bounded; HOME-only filter via Key.
        var assignments = await _db.ShiftAssignments
            .IgnoreQueryFilters()
            .Include(sa => sa.ShiftInstance)
                .ThenInclude(si => si.ShiftType)
            .Include(sa => sa.SourceTimeOffRequest)
            .Where(sa => sa.UserId.HasValue
                && userIds.Contains(sa.UserId.Value)
                && sa.ShiftInstance.WorkDate >= StartDate
                && sa.ShiftInstance.WorkDate <= EndDate
                && (sa.ShiftInstance.ShiftType.Key == ShiftType.KEY_HOME
                    || sa.ShiftInstance.ShiftType.Key == ShiftType.KEY_HOME_PM
                    || sa.ShiftInstance.ShiftType.Key == ShiftType.KEY_HOME_AM))
            .ToListAsync();

        var result = new Dictionary<(int UserId, DateOnly Date), List<HomeShiftItem>>();
        foreach (var a in assignments)
        {
            if (!a.UserId.HasValue) continue;
            var key = (a.UserId.Value, a.ShiftInstance.WorkDate);
            var st = a.ShiftInstance.ShiftType;
            // Use the computed Name (returns "Home" for KEY_HOME). The HOME chip's
            // visual identity is carried by the source/house icons in the partial,
            // so a localized resource lookup isn't required for the chip label.
            var name = st?.Name ?? _localizer["Shift"].Value;
            var item = new HomeShiftItem(
                name,
                st?.Start.ToString("HH:mm", CultureInfo.InvariantCulture),
                st?.End.ToString("HH:mm", CultureInfo.InvariantCulture),
                a.SourceTimeOffRequestId,
                a.SourceTimeOffRequest != null ? (int?)a.SourceTimeOffRequest.Type : null);

            if (!result.TryGetValue(key, out var list))
            {
                list = new List<HomeShiftItem>();
                result[key] = list;
            }
            list.Add(item);
        }

        return result;
    }

    private Dictionary<DateOnly, ExcelCalendarCell> BuildCellsForUser(
        int userId,
        List<Chore> chores,
        Dictionary<(int UserId, DateOnly Date), FyiOverlayData> overlays,
        Dictionary<(int UserId, DateOnly Date), List<(int Id, string Text)>> textEntries,
        Dictionary<(int UserId, DateOnly Date), string> overviewNotes,
        Dictionary<(int UserId, DateOnly Date), List<HomeShiftItem>> homeShifts,
        bool isHebrew)
    {
        var cells = new Dictionary<DateOnly, ExcelCalendarCell>();

        for (var date = StartDate; date <= EndDate; date = date.AddDays(1))
        {
            var cell = new ExcelCalendarCell();

            // Get user's chores for this date
            var userChores = chores
                .Where(c => c.UserId == userId && c.Date == date)
                .ToList();

            cell.Assignments = userChores.Select(c => new ExcelCalendarAssignment
            {
                Id = c.Id,
                Name = c.ChoreType != null ? LocalizeChoreTypeName(c.ChoreType, isHebrew) : c.Title,
                Role = c.ChoreType?.Color, // Use color as role for styling
                UserId = c.UserId
            }).ToList();

            // Task 24: HOME shifts as read-only overlay chips. Id=0 makes them
            // non-removable (no × button). Renders via shared partial as full chip
            // with source/house icons + time range.
            if (homeShifts.TryGetValue((userId, date), out var homeList))
            {
                foreach (var home in homeList)
                {
                    cell.Assignments.Add(new ExcelCalendarAssignment
                    {
                        Id = 0,
                        Name = home.Name,
                        Role = "shift",
                        UserId = userId,
                        IsHome = true,
                        ShiftStart = home.ShiftStart,
                        ShiftEnd = home.ShiftEnd,
                        SourceTimeOffRequestId = home.SourceTimeOffRequestId,
                        SourceTimeOffRequestType = home.SourceTimeOffRequestType
                    });
                }
            }

            // Add overlay data: vacation, on-duty, and other shifts as badges
            // NOTE: Chore overlay items are intentionally skipped — chores are the primary content on this page
            if (overlays.TryGetValue((userId, date), out var overlay))
            {
                // On-duty items rendered as colored assignment chips (Id=0 → non-removable)
                foreach (var duty in overlay.OnDutyItems)
                {
                    cell.Assignments.Add(new ExcelCalendarAssignment
                    {
                        Id = 0,
                        Name = duty.Name,
                        Role = duty.Color ?? "duty"
                    });
                }

                cell.Overlay = new ExcelCalendarOverlay
                {
                    HasVacation = overlay.HasVacation,
                    HasOnDuty = overlay.HasOnDuty,
                    OtherItems = overlay.OtherShifts
                };
            }

            // Text entries rendered as deletable chips (real Id enables × button)
            if (textEntries.TryGetValue((userId, date), out var entries))
            {
                foreach (var entry in entries)
                {
                    cell.Assignments.Add(new ExcelCalendarAssignment
                    {
                        Id = entry.Id,
                        Name = entry.Text,
                        Role = "text-entry"
                    });
                }
            }

            // Overview note rendered as read-only 📋 chip (Id=0 → non-removable)
            if (overviewNotes.TryGetValue((userId, date), out var noteText))
            {
                cell.Assignments.Add(new ExcelCalendarAssignment
                {
                    Id = 0,
                    Name = noteText,
                    Role = "overview-note"
                });
            }

            cells[date] = cell;
        }

        return cells;
    }

    private static string LocalizeChoreTypeName(ChoreType ct, bool isHebrew) =>
        isHebrew && !string.IsNullOrWhiteSpace(ct.NameHe) ? ct.NameHe : ct.NameEn ?? ct.DisplayName;

    // ===============================================================================
    // Justice analytics — in-context drawer (Phase 2)
    // ===============================================================================

    public async Task<IActionResult> OnGetJusticeAsync(CancellationToken ct)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdClaim, out var currentUserId))
            return Forbid();

        if (!MoleculeId.HasValue)
            return new JsonResult(new { error = "no_scope" });

        var allowedMoleculeIds = await _grantService.GetAccessibleMoleculeIdsForGrantAsync(currentUserId, "ViewJusticeTable");
        if (!allowedMoleculeIds.Contains(MoleculeId.Value))
            return Forbid();

        // Period: 30-day window centered on the calendar's anchor.
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var anchor = today;
        if (!string.IsNullOrEmpty(Start) && DateOnly.TryParse(Start, out var parsed)) anchor = parsed;
        var periodStart = anchor.AddDays(-15);
        var periodEnd = anchor.AddDays(15);

        var query = new JusticeQuery(
            Scope: JusticeScope.Molecule,
            ScopeId: MoleculeId,
            PeriodStart: periodStart,
            PeriodEnd: periodEnd,
            WorkType: JusticeWorkType.Chore,
            ExcludeExemptShifts: false,                  // chores have no exempt analogue
            Level: JusticeLevel.CompaniesInMolecule);

        var view = await _justiceService.GetInContextViewAsync(query, ct);
        return new JsonResult(BuildJusticeJson(view));
    }

    private object BuildJusticeJson(InContextJusticeViewModel view)
    {
        return new
        {
            query = new
            {
                scope = view.Query.Scope.ToString(),
                scopeId = view.Query.ScopeId,
                level = view.Query.Level.ToString(),
                workType = view.Query.WorkType.ToString(),
                periodStart = view.Query.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                periodEnd = view.Query.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            },
            spreadIndex = view.SpreadIndex,
            spreadSeverity = view.SpreadSeverity,
            spreadLabel = _localizer[view.SpreadLabelKey].Value,
            mostOver = view.MostOver is null ? null : new { name = view.MostOver.Name, deviationPercent = view.MostOver.DeviationPercent },
            mostUnder = view.MostUnder is null ? null : new { name = view.MostUnder.Name, deviationPercent = view.MostUnder.DeviationPercent },
            rows = view.Rows.Select(r => new { id = r.Id, name = r.Name, actual = r.Actual, expected = r.Expected, deviationPercent = r.DeviationPercent, band = r.Band.ToString() }),
            maxRibbonValue = view.MaxRibbonValue,
            whereToFocus = view.WhereToFocus.Select(h => new { kind = h.Kind, shiftInstanceId = h.ShiftInstanceId, date = h.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), label = h.Label, deficit = h.Deficit, companyId = h.CompanyId, jobTypeId = h.JobTypeId, dutyTypeValue = h.DutyTypeValue, userId = h.UserId, moleculeId = MoleculeId }),
            fullViewUrl = view.FullViewUrl,
            noneLabel = _localizer["Justice_None"].Value,
            noHolesLabel = _localizer["Justice_Panel_NoHolesChores"].Value
        };
    }

    // Phase 2d helper: row IDs on chore/onduty calendars are prefixed strings ("user-42", "dutytype-0").
    // The eligibility handler accepts the raw value to keep frontend semantics simple; this helper
    // strips the prefix server-side and rejects mismatches with a 400 to prevent cross-row-kind probes.
    private static bool TryParseRowId(string? raw, string expectedPrefix, out int id)
    {
        id = 0;
        if (string.IsNullOrEmpty(raw)) return false;
        var prefix = expectedPrefix + "-";
        if (!raw.StartsWith(prefix, StringComparison.Ordinal)) return false;
        return int.TryParse(raw.AsSpan(prefix.Length), out id);
    }

    /// <summary>
    /// Phase 2d — JSON endpoint that ranks candidate users to fill a specific chore hole.
    /// Inputs: <paramref name="rowId"/> is the Chore-calendar row identifier ("user-{userId}"),
    /// <paramref name="date"/> is the target date.
    /// SECURITY-AUDITED: MoleculeId is page-model-only — never accepted from query string here
    /// because that would be an IDOR vector (caller could probe unauthorized molecules).
    /// </summary>
    public async Task<IActionResult> OnGetJusticeEligibilityAsync(string? rowId, string? date, CancellationToken ct)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdClaim, out var currentUserId))
            return Forbid();

        if (!MoleculeId.HasValue)
            return new JsonResult(new { error = "no_scope" });

        var allowedMoleculeIds = await _grantService.GetAccessibleMoleculeIdsForGrantAsync(currentUserId, "ViewJusticeTable");
        if (!allowedMoleculeIds.Contains(MoleculeId.Value))
            return Forbid();

        if (!TryParseRowId(rowId, "user", out var targetUserId))
            return BadRequest(new { error = "invalid_row_id" });

        if (string.IsNullOrEmpty(date) || !DateOnly.TryParse(date, out var workDate))
            return BadRequest(new { error = "invalid_date" });

        // Defense-in-depth: verify the target user's company is in this molecule.
        // SECURITY-AUDITED: Chore.MoleculeId is direct on the entity; this join confirms the
        // candidate user belongs to the requested molecule. MoleculeId is sourced from the page
        // model — never from user input.
        var targetCompanyId = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.Id == targetUserId)
            .Select(u => (int?)u.CompanyId)
            .FirstOrDefaultAsync(ct);
        if (targetCompanyId is null) return BadRequest(new { error = "invalid_user" });

        var targetMoleculeId = await _db.Companies
            .IgnoreQueryFilters()
            .Where(c => c.Id == targetCompanyId.Value)
            .Select(c => c.MoleculeId)
            .FirstOrDefaultAsync(ct);
        if (targetMoleculeId != MoleculeId.Value)
            return Forbid();

        DateOnly periodStart, periodEnd;
        ResolveJusticePeriod(out periodStart, out periodEnd);

        var parentScope = new JusticeQuery(
            Scope: JusticeScope.Company,
            ScopeId: targetCompanyId.Value,
            PeriodStart: periodStart,
            PeriodEnd: periodEnd,
            WorkType: JusticeWorkType.Chore,
            ExcludeExemptShifts: false,
            Level: JusticeLevel.UsersInCompany);

        var hole = new HoleSelector(
            Kind: "chore",
            ShiftInstanceId: null,
            Date: workDate,
            CompanyId: targetCompanyId.Value,
            JobTypeId: null,
            DutyTypeValue: null,
            MoleculeId: MoleculeId.Value,
            ParentScope: parentScope);

        var result = await _justiceService.GetEligibleCandidatesAsync(hole, ct);
        return new JsonResult(BuildChoreEligibilityJson(result, workDate, targetUserId));
    }

    /// <summary>
    /// Phase 2d — JSON endpoint previewing impact of a hypothetical chore assignment.
    /// </summary>
    public async Task<IActionResult> OnGetJusticePreviewAsync(int userId, string? date, CancellationToken ct)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdClaim, out var currentUserId))
            return Forbid();

        if (!MoleculeId.HasValue)
            return new JsonResult(new { error = "no_scope" });

        var allowedMoleculeIds = await _grantService.GetAccessibleMoleculeIdsForGrantAsync(currentUserId, "ViewJusticeTable");
        if (!allowedMoleculeIds.Contains(MoleculeId.Value))
            return Forbid();

        if (string.IsNullOrEmpty(date) || !DateOnly.TryParse(date, out var workDate))
            return BadRequest(new { error = "invalid_date" });

        var targetCompanyId = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .Select(u => (int?)u.CompanyId)
            .FirstOrDefaultAsync(ct);
        if (targetCompanyId is null) return BadRequest(new { error = "invalid_user" });

        var targetMoleculeId = await _db.Companies
            .IgnoreQueryFilters()
            .Where(c => c.Id == targetCompanyId.Value)
            .Select(c => c.MoleculeId)
            .FirstOrDefaultAsync(ct);
        if (targetMoleculeId != MoleculeId.Value)
            return Forbid();

        DateOnly periodStart, periodEnd;
        ResolveJusticePeriod(out periodStart, out periodEnd);

        var parentScope = new JusticeQuery(
            Scope: JusticeScope.Company,
            ScopeId: targetCompanyId.Value,
            PeriodStart: periodStart,
            PeriodEnd: periodEnd,
            WorkType: JusticeWorkType.Chore,
            ExcludeExemptShifts: false,
            Level: JusticeLevel.UsersInCompany);

        var hole = new HoleSelector(
            Kind: "chore",
            ShiftInstanceId: null,
            Date: workDate,
            CompanyId: targetCompanyId.Value,
            JobTypeId: null,
            DutyTypeValue: null,
            MoleculeId: MoleculeId.Value,
            ParentScope: parentScope);

        var preview = await _justiceService.PreviewImpactAsync(new SimulatedAssignment(userId, hole), ct);
        return new JsonResult(BuildPreviewJson(preview));
    }

    private void ResolveJusticePeriod(out DateOnly start, out DateOnly end)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var anchor = today;
        if (!string.IsNullOrEmpty(Start) && DateOnly.TryParse(Start, out var parsed)) anchor = parsed;
        start = anchor.AddDays(-15);
        end = anchor.AddDays(15);
    }

    private object BuildChoreEligibilityJson(EligibleCandidatesViewModel result, DateOnly date, int targetUserId)
    {
        return new
        {
            kind = "chore",
            date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            moleculeId = MoleculeId,
            targetUserId,
            candidates = result.Candidates.Select(c => new
            {
                userId = c.UserId,
                displayName = c.DisplayName,
                avatarUrl = c.AvatarUrl,
                actual = c.Actual,
                expected = c.Expected,
                deviationPercent = c.DeviationPercent,
                band = c.Band.ToString(),
                isHardBlocked = c.Eligibility.IsHardBlocked,
                warnings = c.Eligibility.WarningKeys
            }),
            hardBlocked = result.HardBlocked.Select(c => new
            {
                userId = c.UserId,
                displayName = c.DisplayName,
                avatarUrl = c.AvatarUrl,
                band = c.Band.ToString(),
                hardBlockReason = c.Eligibility.HardBlockReason
            })
        };
    }

    private static object BuildPreviewJson(ImpactPreviewViewModel p) => new
    {
        spreadIndexBefore = p.SpreadIndexBefore,
        spreadSeverityBefore = p.SpreadSeverityBefore,
        spreadIndexAfter = p.SpreadIndexAfter,
        spreadSeverityAfter = p.SpreadSeverityAfter,
        candidateActualBefore = p.CandidateActualBefore,
        candidateActualAfter = p.CandidateActualAfter,
        candidateDeviationBefore = p.CandidateDeviationBefore,
        candidateDeviationAfter = p.CandidateDeviationAfter
    };
}
