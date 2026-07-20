using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using ShiftManager.ViewComponents;
using System.Security.Claims;

namespace ShiftManager.Pages.Calendar;

/// <summary>
/// Excel-style On-Call (Day Shifts) Calendar page - Shows duty types as rows, dates as columns.
/// On-Duty assignments are global (cross-company), optionally filtered by area.
/// </summary>
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires [Authorize];
// OnDuty is global by design; user lookup scoped by area-based company membership
[Authorize]
public class OnCallModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IOnDutyService _onDutyService;
    private readonly IGrantService _grantService;
    private readonly ICompanyContext _companyContext;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly ILogger<OnCallModel> _logger;
    private readonly ICalendarTextEntryService _textEntryService;
    private readonly IJusticeService _justiceService;
    private readonly IDraftDutyService _draftDutyService;
    private readonly IDraftLifecycle _draftLifecycle;

    public OnCallModel(
        AppDbContext db,
        IOnDutyService onDutyService,
        IGrantService grantService,
        ICompanyContext companyContext,
        IStringLocalizer<SharedResources> localizer,
        ILogger<OnCallModel> logger,
        ICalendarTextEntryService textEntryService,
        IJusticeService justiceService,
        IDraftDutyService draftDutyService,
        IDraftLifecycle draftLifecycle)
    {
        _db = db;
        _onDutyService = onDutyService;
        _grantService = grantService;
        _companyContext = companyContext;
        _localizer = localizer;
        _logger = logger;
        _textEntryService = textEntryService;
        _justiceService = justiceService;
        _draftDutyService = draftDutyService;
        _draftLifecycle = draftLifecycle;
    }

    // Query parameters
    [BindProperty(SupportsGet = true)]
    public int? AreaId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Start { get; set; }

    [BindProperty(SupportsGet = true)]
    public string ViewMode { get; set; } = "week"; // week, 2weeks, month

    [BindProperty(SupportsGet = true)]
    public int? DutyTypeFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool JustMine { get; set; }

    /// <summary>When true, the on-call calendar renders the viewer's private draft overlay (sub-project D).</summary>
    [BindProperty(SupportsGet = true)]
    public bool DraftMode { get; set; }

    /// <summary>The active on-call draft session id for the current viewer + area + week (null when not drafting).</summary>
    public int? ActiveDraftId { get; set; }

    // Page properties
    public ExcelCalendarTableViewModel CalendarData { get; set; } = new();
    public bool CanEdit { get; set; }
    // Grant NameKeys that would unlock editing when read-only; empty when CanEdit. Drives the "?" help.
    public List<string> RequiredGrantNameKeys { get; set; } = new();
    // See Shifts.cshtml.cs for rationale: quick-entry toggle visible to any user with WriteOverviewNotes.
    public bool CanWriteNote { get; set; }
    public List<Area> AvailableAreas { get; set; } = new();
    public Area? SelectedArea { get; set; }
    public List<DutyTypeInfo> DutyTypes { get; set; } = new();
    public int CurrentUserId { get; set; }
    public List<AppUser> Users { get; set; } = new();

    /// <summary>
    /// Company display name per company id, for the assignee picker. The on-call pool is area-wide
    /// (many companies), so identical display names collide; the picker appends the company to
    /// disambiguate (e.g. "רועי שלום — חמסה").
    /// </summary>
    public Dictionary<int, string> CompanyNamesById { get; set; } = new();

    // Navigation
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string PreviousStart { get; set; } = string.Empty;
    public string NextStart { get; set; } = string.Empty;

    /// <summary>
    /// Info about a duty type (built-in or custom)
    /// </summary>
    public class DutyTypeInfo
    {
        public int TypeValue { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public bool IsBackupType { get; set; }
        public int? PrimaryTypeValue { get; set; } // Links backup to its primary
    }

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

        // Get user's company
        var companyId = _companyContext.CompanyId;
        if (!companyId.HasValue)
        {
            _logger.LogWarning("User {UserId} has no company context", currentUserId);
            return RedirectToPage("/Error");
        }

        var userCompany = await _db.Companies
            .Include(c => c.Molecule)
            .ThenInclude(m => m != null ? m.Area : null)
            .FirstOrDefaultAsync(c => c.Id == companyId.Value);

        // Get user's area through the hierarchy: Company -> Molecule -> Area
        var userAreaId = userCompany?.Molecule?.AreaId;

        // Load available areas (for filtering, On-Duty is global but we can filter view by area)
        await LoadAvailableAreasAsync(currentUserId);

        // Set default AreaId if not provided and user has one
        if (!AreaId.HasValue && userAreaId != null)
        {
            AreaId = userAreaId;
        }

        // Validate selected area is accessible
        if (AreaId.HasValue && !AvailableAreas.Any(a => a.Id == AreaId))
        {
            AreaId = userAreaId;
        }

        SelectedArea = AvailableAreas.FirstOrDefault(a => a.Id == AreaId);

        // Load duty types (built-in + custom)
        await LoadDutyTypesAsync();

        // Calculate date range
        CalculateDateRange();

        // Check edit permission — any of these grants authorizes OnCall edits
        // (assign/unassign users + quick-entry text notes). Matches the OR chain
        // in OnDutyService.CanUserManageOnDutyAsync. Widened 2026-04-15 to include
        // EditOnCallCalendar (collaborative editing for hakam-eligible users).
        CanEdit = await _grantService.HasGrantAsync(currentUserId, "ManageOnDuty")
            || await _grantService.HasGrantAsync(currentUserId, "AssignHakamDuties")
            || await _grantService.HasGrantAsync(currentUserId, "AssignKatzinDuties")
            || await _grantService.HasGrantAsync(currentUserId, "EditOnCallCalendar");
        CanWriteNote = CanEdit || await _grantService.HasGrantAsync(currentUserId, "WriteOverviewNotes");

        // When read-only, surface which on-call grant(s) the user is missing in the "?" help button.
        RequiredGrantNameKeys = CanEdit
            ? new List<string>()
            : await _grantService.GetGrantNameKeysAsync(
                "ManageOnDuty", "AssignHakamDuties", "AssignKatzinDuties", "EditOnCallCalendar");

        // Build calendar data (duty types as rows, dates as columns)
        await BuildDutyTypeBasedCalendarAsync();

        _logger.LogInformation(
            "On-Call calendar loaded for User {UserId}, Area {AreaId}, ViewMode {ViewMode}",
            currentUserId, AreaId, ViewMode);

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

        PreviousStart = StartDate.AddDays(-daysToMove).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        NextStart = StartDate.AddDays(daysToMove).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static DateOnly GetStartOfWeek(DateOnly date)
    {
        int daysFromSunday = (int)date.DayOfWeek;
        return date.AddDays(-daysFromSunday);
    }

    private async Task LoadAvailableAreasAsync(int userId)
    {
        // Load all active areas - On-Duty is global but we allow area filtering for the view
        // Users with Director+ role see all areas, others see their company's area
        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            AvailableAreas = new List<Area>();
            return;
        }

        var canViewAllAreas = await _grantService.HasGrantAsync(userId, "ViewAllAreas");
        if (canViewAllAreas)
        {
            // Users with ViewAllAreas grant see all active areas
            AvailableAreas = await _db.Areas
                .Where(a => a.IsActive)
                .OrderBy(a => a.DisplayName)
                .ToListAsync();
        }
        else
        {
            // Others see areas from their grants or their own company's area (via Molecule)
            var company = await _db.Companies
                .Include(c => c.Molecule)
                .FirstOrDefaultAsync(c => c.Id == user.CompanyId);
            if (company?.Molecule?.AreaId != null)
            {
                var area = await _db.Areas.FirstOrDefaultAsync(a => a.Id == company.Molecule.AreaId && a.IsActive);
                if (area != null)
                {
                    AvailableAreas = new List<Area> { area };
                }
            }

            // Also add areas from grants
            var userGrants = await _grantService.GetUserGrantsAsync(userId);
            var grantAreaIds = userGrants
                .Where(g => g.AreaId.HasValue)
                .Select(g => g.AreaId!.Value)
                .Distinct()
                .ToList();

            if (grantAreaIds.Any())
            {
                var grantAreas = await _db.Areas
                    .Where(a => grantAreaIds.Contains(a.Id) && a.IsActive)
                    .ToListAsync();

                foreach (var area in grantAreas)
                {
                    if (!AvailableAreas.Any(a => a.Id == area.Id))
                    {
                        AvailableAreas.Add(area);
                    }
                }

                AvailableAreas = AvailableAreas.OrderBy(a => a.DisplayName).ToList();
            }
        }
    }

    private async Task LoadDutyTypesAsync()
    {
        DutyTypes = new List<DutyTypeInfo>();

        // Add built-in types
        DutyTypes.Add(new DutyTypeInfo
        {
            TypeValue = (int)OnDutyType.Hakam,
            Name = _localizer["Hakam"],
            Icon = "shield-check",
            Color = "#8B4513", // Saddle Brown from design tokens
            IsBackupType = false
        });

        DutyTypes.Add(new DutyTypeInfo
        {
            TypeValue = (int)OnDutyType.Lead,
            Name = _localizer["Lead"],
            Icon = "star",
            Color = "#1E3A5F", // Deep Navy from design tokens
            IsBackupType = false
        });

        // Load custom types from OnDutyTypeConfig
        var customTypes = await _db.OnDutyTypeConfigs
            .Where(t => t.IsActive)
            .OrderBy(t => t.TypeValue)
            .ToListAsync();

        foreach (var customType in customTypes)
        {
            var isBackup = customType.NameEn.Contains("Backup", StringComparison.OrdinalIgnoreCase) ||
                           customType.NameHe.Contains("רזרבה", StringComparison.OrdinalIgnoreCase);

            DutyTypes.Add(new DutyTypeInfo
            {
                TypeValue = customType.TypeValue,
                Name = Thread.CurrentThread.CurrentUICulture.Name.StartsWith("he", StringComparison.Ordinal)
                    ? customType.NameHe
                    : customType.NameEn,
                Icon = customType.Icon,
                Color = customType.Color,
                IsBackupType = isBackup,
                PrimaryTypeValue = isBackup ? (int)OnDutyType.Hakam : null // Backup-hakam links to Hakam
            });
        }
    }

    private async Task BuildDutyTypeBasedCalendarAsync()
    {
        // Get all on-duty assignments for the date range
        var onDuties = await _onDutyService.GetOnDutiesAsync(
            startDate: StartDate,
            endDate: EndDate,
            includeCanceled: false);

        // If area filter is set, filter by users in that area (via Molecule)
        if (AreaId.HasValue)
        {
            // Get molecules in this area
            var moleculeIdsInArea = await _db.Molecules
                .Where(m => m.AreaId == AreaId.Value)
                .Select(m => m.Id)
                .ToListAsync();

            // Get companies in those molecules
            var companyIdsInArea = await _db.Companies
                .Where(c => c.MoleculeId.HasValue && moleculeIdsInArea.Contains(c.MoleculeId.Value))
                .Select(c => c.Id)
                .ToListAsync();

            onDuties = onDuties
                .Where(o => o.User != null && companyIdsInArea.Contains(o.User.CompanyId))
                .ToList();

            // Load users for bottom-sheet dropdown
            Users = await _db.Users
                .IgnoreQueryFilters()
                .Where(u => companyIdsInArea.Contains(u.CompanyId) && u.IsActive)
                .OrderBy(u => u.DisplayName)
                .Select(u => new AppUser { Id = u.Id, DisplayName = u.DisplayName, CompanyId = u.CompanyId })
                .ToListAsync();
        }
        else
        {
            // No area filter — load all active users for dropdown
            Users = await _db.Users
                .IgnoreQueryFilters()
                .Where(u => u.IsActive)
                .OrderBy(u => u.DisplayName)
                .Select(u => new AppUser { Id = u.Id, DisplayName = u.DisplayName, CompanyId = u.CompanyId })
                .ToListAsync();
        }

        // Company display names for the assignee picker (disambiguates duplicate names across the area).
        var pickerCompanyIds = Users.Select(u => u.CompanyId).Distinct().ToList();
        CompanyNamesById = await _db.Companies
            .IgnoreQueryFilters()
            .Where(c => pickerCompanyIds.Contains(c.Id))
            .Select(c => new { c.Id, c.DisplayName })
            .ToDictionaryAsync(c => c.Id, c => c.DisplayName);

        // If "Just Mine" filter is set, filter by current user
        if (JustMine)
        {
            onDuties = onDuties.Where(o => o.UserId == CurrentUserId).ToList();
        }

        // Apply duty type filter if set
        if (DutyTypeFilter.HasValue)
        {
            onDuties = onDuties.Where(o => (int)o.Type == DutyTypeFilter.Value).ToList();
        }

        // Draft Mode (sub-project D): overlay the viewer's private staged cells onto the live set. Applied
        // AFTER the area filter so staged users the assigner picked (globally) are never hidden by the area
        // view. Sets ActiveDraftId.
        onDuties = await ApplyDraftOverlayAsync(onDuties);

        // Load text entries for overlay badges (cross-company via IgnoreQueryFilters)
        var assignedOnDutyUserIds = onDuties.Select(o => o.UserId).Distinct();
        var allEntriesWithType = await _textEntryService.GetForUsersAndDateRangeWithTypeAsync(
            assignedOnDutyUserIds, StartDate, EndDate);
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

        // Group duty types by primary/backup relationship
        var primaryTypes = DutyTypes.Where(dt => !dt.IsBackupType).ToList();
        var backupTypes = DutyTypes.Where(dt => dt.IsBackupType).ToList();

        // Build rows - one per duty type
        var rows = new List<ExcelCalendarRow>();
        foreach (var dutyType in DutyTypes)
        {
            var row = new ExcelCalendarRow
            {
                Id = $"dutytype-{dutyType.TypeValue}",
                Label = dutyType.Name,
                Icon = dutyType.Icon,
                Color = dutyType.Color
            };

            // Build cells for each date
            row.Cells = BuildCellsForDutyType(dutyType.TypeValue, onDuties, textEntries, overviewNotes);
            rows.Add(row);
        }

        CalendarData = new ExcelCalendarTableViewModel
        {
            StartDate = StartDate,
            EndDate = EndDate,
            ViewMode = ViewMode,
            IsReadOnly = !CanEdit,
            CalendarType = "oncall",
            Rows = rows,
            Groups = null, // No grouping for duty types
            RequiredGrantNameKeys = RequiredGrantNameKeys
        };
        CalendarData.RowMode = "Duty";
        // Draft Mode (sub-project D): when set, the shared row template routes every × on a duty chip
        // (real baseline OR synthetic staged) to the coordinate-keyed draft-clear handler.
        CalendarData.DraftSessionId = ActiveDraftId;
        // +1 for the <thead> column-header row (ARIA 1.2 §6.6.4).
        CalendarData.TotalRows = CalendarData.Rows.Count + (CalendarData.Groups?.Count ?? 0) + 1;
        CalendarData.RowOrderContextKey = AreaId.HasValue ? $"oncall:{AreaId.Value}" : "oncall:all";
    }

    /// <summary>
    /// Draft Mode (sub-project D): replace the live users of each TOUCHED (dutyType, date) cell with the
    /// draft's staged set, rendered as synthetic (Id=0) <see cref="OnDuty"/> rows so the assigner sees their
    /// private sandbox. The staged set is GLOBAL (Spec D §3) — staged users are shown regardless of the area
    /// view. Returns the input unchanged when not drafting or no active draft exists.
    /// </summary>
    internal async Task<List<OnDuty>> ApplyDraftOverlayAsync(List<OnDuty> live)
    {
        if (!DraftMode)
            return live;

        var draft = await _draftDutyService.GetActiveDraftAsync(CurrentUserId, AreaId, StartDate);
        if (draft == null)
            return live;
        ActiveDraftId = draft.Id;

        var overlay = (await _draftDutyService.GetDutyOverlayAsync(draft.Id))
            .Where(o => o.WorkDate >= StartDate && o.WorkDate <= EndDate
                        && (!DutyTypeFilter.HasValue || o.DutyTypeValue == DutyTypeFilter.Value))
            .ToList();
        if (overlay.Count == 0)
            return live;

        var touched = overlay.ToDictionary(o => (o.DutyTypeValue, o.WorkDate), o => o.UserIds);

        // Drop the live rows of touched cells; keep everything else (untouched cells render live).
        var result = live.Where(o => !touched.ContainsKey(((int)o.Type, o.Date))).ToList();

        // Load display names for the staged users (cross-company/global — IgnoreQueryFilters).
        var stagedIds = touched.Values.SelectMany(x => x).Distinct().ToList();
        var nameMap = stagedIds.Count == 0
            ? new Dictionary<int, string>()
            : await _db.Users.IgnoreQueryFilters()
                .Where(u => stagedIds.Contains(u.Id))
                .Select(u => new { u.Id, u.DisplayName })
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName);

        foreach (var ((dutyTypeValue, date), userIds) in touched)
        {
            foreach (var uid in userIds)
            {
                result.Add(new OnDuty
                {
                    Id = 0, // synthetic — not a persisted row; the chip's × routes to draft-clear by coordinate
                    UserId = uid,
                    Type = (OnDutyType)dutyTypeValue,
                    Date = date,
                    User = new AppUser { Id = uid, DisplayName = nameMap.GetValueOrDefault(uid, _localizer["Unknown"]) }
                });
            }
        }

        return result;
    }

    internal Dictionary<DateOnly, ExcelCalendarCell> BuildCellsForDutyType(
        int dutyTypeValue,
        List<OnDuty> onDuties,
        Dictionary<(int UserId, DateOnly Date), List<(int Id, string Text)>> textEntries,
        Dictionary<(int UserId, DateOnly Date), string> overviewNotes)
    {
        var cells = new Dictionary<DateOnly, ExcelCalendarCell>();

        for (var date = StartDate; date <= EndDate; date = date.AddDays(1))
        {
            var cell = new ExcelCalendarCell();

            // Get on-duty assignments for this type and date
            var assignments = onDuties
                .Where(o => (int)o.Type == dutyTypeValue && o.Date == date)
                .ToList();

            cell.Assignments = assignments.Select(o => new ExcelCalendarAssignment
            {
                Id = o.Id,
                Name = o.User?.DisplayName ?? _localizer["Unknown"],
                Role = o.Notes, // Use notes as additional info
                UserId = o.UserId,
                AssignmentTooltip = o.Creator != null
                    ? string.Format(CultureInfo.CurrentCulture, _localizer["OnDuty_AssignedByTooltip"].Value, o.Creator.DisplayName, o.CreatedAt.ToString("d", CultureInfo.CurrentCulture))
                    : null
            }).ToList();

            // Text entry overlay badge: show 📝 if any assigned user has text entries
            var cellTextEntryTexts = new List<string>();
            foreach (var assignment in assignments)
            {
                if (textEntries.TryGetValue((assignment.UserId, date), out var entries))
                    cellTextEntryTexts.AddRange(entries.Select(e => e.Text));
            }
            if (cellTextEntryTexts.Count > 0)
            {
                cell.Overlay ??= new ExcelCalendarOverlay();
                cell.Overlay.HasTextEntry = true;
                cell.Overlay.TextEntryTexts = cellTextEntryTexts;
            }

            // Overview note overlay badge: show 📋 with aggregated tooltip for all assigned users
            var cellNoteTexts = new List<string>();
            foreach (var assignment in assignments)
            {
                if (overviewNotes.TryGetValue((assignment.UserId, date), out var noteText))
                    cellNoteTexts.Add(noteText);
            }
            if (cellNoteTexts.Count > 0)
            {
                cell.Overlay ??= new ExcelCalendarOverlay();
                cell.Overlay.HasOverviewNote = true;
                cell.Overlay.OverviewNoteText = string.Join("\n", cellNoteTexts);
            }

            cells[date] = cell;
        }

        return cells;
    }

    // ===============================================================================
    // Justice analytics — in-context drawer (Phase 2)
    // ===============================================================================

    public async Task<IActionResult> OnGetJusticeAsync(CancellationToken ct)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdClaim, out var currentUserId))
            return Forbid();

        if (!AreaId.HasValue)
            return new JsonResult(new { error = "no_scope" });

        // OnCall is area-scoped. The ViewJusticeTable grant resolves accessible MOLECULES;
        // we accept the area if at least one accessible molecule lives within it.
        var allowedMoleculeIds = await _grantService.GetAccessibleMoleculeIdsForGrantAsync(currentUserId, "ViewJusticeTable");
        var areaIsReachable = await _db.Molecules
            .IgnoreQueryFilters()  // SECURITY: Justice viewer may span tenants; molecule list gated by grant.
            .AnyAsync(m => allowedMoleculeIds.Contains(m.Id) && m.AreaId == AreaId.Value, ct);
        if (!areaIsReachable) return Forbid();

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var anchor = today;
        if (!string.IsNullOrEmpty(Start) && DateOnly.TryParse(Start, out var parsed)) anchor = parsed;
        var periodStart = anchor.AddDays(-15);
        var periodEnd = anchor.AddDays(15);

        var query = new JusticeQuery(
            Scope: JusticeScope.Area,
            ScopeId: AreaId,
            PeriodStart: periodStart,
            PeriodEnd: periodEnd,
            WorkType: JusticeWorkType.OnDuty,
            ExcludeExemptShifts: false,                  // on-duty has no exempt analogue
            Level: JusticeLevel.MoleculesInArea);

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
            whereToFocus = view.WhereToFocus.Select(h => new { kind = h.Kind, shiftInstanceId = h.ShiftInstanceId, date = h.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), label = h.Label, deficit = h.Deficit, companyId = h.CompanyId, jobTypeId = h.JobTypeId, dutyTypeValue = h.DutyTypeValue, userId = h.UserId, moleculeId = (int?)null }),
            fullViewUrl = view.FullViewUrl,
            noneLabel = _localizer["Justice_None"].Value,
            noHolesLabel = _localizer["Justice_Panel_NoHolesOnDuty"].Value
        };
    }

    private static bool TryParseRowId(string? raw, string expectedPrefix, out int id)
    {
        id = 0;
        if (string.IsNullOrEmpty(raw)) return false;
        var prefix = expectedPrefix + "-";
        if (!raw.StartsWith(prefix, StringComparison.Ordinal)) return false;
        return int.TryParse(raw.AsSpan(prefix.Length), out id);
    }

    /// <summary>
    /// Phase 2d — JSON endpoint that ranks candidate users for a specific (date, dutyType) hole.
    /// SECURITY-AUDITED: moleculeId derived from the caller's own company — never accepted from
    /// query string. OnDuty is global by design; molecule scoping here is for Justice fairness math
    /// (which is per-molecule), not for entity-level access control.
    /// </summary>
    public async Task<IActionResult> OnGetJusticeEligibilityAsync(string? rowId, string? date, CancellationToken ct)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdClaim, out var currentUserId))
            return Forbid();

        if (!TryParseRowId(rowId, "dutytype", out var dutyTypeValue))
            return BadRequest(new { error = "invalid_row_id" });

        if (!await _onDutyService.IsValidDutyTypeAsync(dutyTypeValue))
            return BadRequest(new { error = "invalid_duty_type" });

        if (string.IsNullOrEmpty(date) || !DateOnly.TryParse(date, out var workDate))
            return BadRequest(new { error = "invalid_date" });

        // Derive moleculeId from caller's own company, never from query string.
        // SECURITY-AUDITED: caller's CompanyId comes from the tenant resolver claim; the molecule
        // we scope Justice to is the one the caller belongs to. If the caller's company has no
        // molecule (legacy data), abort.
        var callerCompanyId = _companyContext.CompanyId;
        if (!callerCompanyId.HasValue)
            return new JsonResult(new { error = "no_scope" });

        var moleculeId = await _db.Companies
            .IgnoreQueryFilters()
            .Where(c => c.Id == callerCompanyId.Value)
            .Select(c => c.MoleculeId)
            .FirstOrDefaultAsync(ct);
        if (moleculeId is null)
            return new JsonResult(new { error = "no_molecule" });

        var allowedMoleculeIds = await _grantService.GetAccessibleMoleculeIdsForGrantAsync(currentUserId, "ViewJusticeTable");
        if (!allowedMoleculeIds.Contains(moleculeId.Value))
            return Forbid();

        DateOnly periodStart, periodEnd;
        ResolveJusticePeriod(out periodStart, out periodEnd);

        var parentScope = new JusticeQuery(
            Scope: JusticeScope.Molecule,
            ScopeId: moleculeId.Value,
            PeriodStart: periodStart,
            PeriodEnd: periodEnd,
            WorkType: JusticeWorkType.OnDuty,
            ExcludeExemptShifts: false,
            Level: JusticeLevel.UsersInCompany);

        var hole = new HoleSelector(
            Kind: "onduty",
            ShiftInstanceId: null,
            Date: workDate,
            CompanyId: null,
            JobTypeId: null,
            DutyTypeValue: dutyTypeValue,
            MoleculeId: moleculeId.Value,
            ParentScope: parentScope);

        var result = await _justiceService.GetEligibleCandidatesAsync(hole, ct);
        return new JsonResult(BuildOnDutyEligibilityJson(result, workDate, dutyTypeValue, moleculeId.Value));
    }

    /// <summary>
    /// Phase 2d — JSON endpoint previewing impact of a hypothetical on-duty assignment.
    /// </summary>
    public async Task<IActionResult> OnGetJusticePreviewAsync(int userId, int? dutyTypeValue, string? date, CancellationToken ct)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdClaim, out var currentUserId))
            return Forbid();

        if (!dutyTypeValue.HasValue)
            return BadRequest(new { error = "missing_duty_type" });

        if (string.IsNullOrEmpty(date) || !DateOnly.TryParse(date, out var workDate))
            return BadRequest(new { error = "invalid_date" });

        var callerCompanyId = _companyContext.CompanyId;
        if (!callerCompanyId.HasValue)
            return new JsonResult(new { error = "no_scope" });

        var moleculeId = await _db.Companies
            .IgnoreQueryFilters()
            .Where(c => c.Id == callerCompanyId.Value)
            .Select(c => c.MoleculeId)
            .FirstOrDefaultAsync(ct);
        if (moleculeId is null)
            return new JsonResult(new { error = "no_molecule" });

        var allowedMoleculeIds = await _grantService.GetAccessibleMoleculeIdsForGrantAsync(currentUserId, "ViewJusticeTable");
        if (!allowedMoleculeIds.Contains(moleculeId.Value))
            return Forbid();

        DateOnly periodStart, periodEnd;
        ResolveJusticePeriod(out periodStart, out periodEnd);

        var parentScope = new JusticeQuery(
            Scope: JusticeScope.Molecule,
            ScopeId: moleculeId.Value,
            PeriodStart: periodStart,
            PeriodEnd: periodEnd,
            WorkType: JusticeWorkType.OnDuty,
            ExcludeExemptShifts: false,
            Level: JusticeLevel.UsersInCompany);

        var hole = new HoleSelector(
            Kind: "onduty",
            ShiftInstanceId: null,
            Date: workDate,
            CompanyId: null,
            JobTypeId: null,
            DutyTypeValue: dutyTypeValue.Value,
            MoleculeId: moleculeId.Value,
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

    private object BuildOnDutyEligibilityJson(EligibleCandidatesViewModel result, DateOnly date, int dutyTypeValue, int moleculeId)
    {
        return new
        {
            kind = "onduty",
            date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            dutyTypeValue,
            moleculeId,
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

    // ===============================================================================
    // Draft Mode (sub-project D) — on-call staging + commit page handlers.
    // On-call writes go to /Api/ today (no page-handler write host), so the draft handlers live HERE and the
    // JS branches to them BEFORE the live /Api call when window.__draftSessionId is set (Spec D §4).
    // ===============================================================================

    private int? CurrentUserIdOrNull()
        => int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : (int?)null;

    private Task<bool> OwnsActiveDraftAsync(int draftSessionId, int userId)
        => _db.DraftSessions.AnyAsync(d => d.Id == draftSessionId && d.OwnerUserId == userId && d.Status == DraftSessionStatus.Active);

    public async Task<IActionResult> OnPostEnterDraftAsync([FromBody] EnterOnCallDraftRequest request)
    {
        if (CurrentUserIdOrNull() is not int userId)
            return new JsonResult(new { success = false }) { StatusCode = 401 };
        // Draft-entry auth gate = the on-call OR-chain (ManageOnDuty || AssignHakamDuties ||
        // AssignKatzinDuties || EditOnCallCalendar). Commit RE-authorizes per cell (Spec D §4-5).
        if (!await _onDutyService.CanUserManageOnDutyAsync(userId))
            return new JsonResult(new { success = false }) { StatusCode = 403 };

        var scope = new DraftScope(DraftSurface.OnCall, MoleculeId: null, JobTypeId: null,
            AreaId: request.AreaId, request.WeekStart, request.WeekEnd);
        var draft = await _draftLifecycle.EnterAsync(userId, scope);
        return new JsonResult(new { success = true, draftSessionId = draft.Id });
    }

    public async Task<IActionResult> OnPostDraftDutyAssignAsync([FromBody] DraftDutyRequest request)
    {
        if (CurrentUserIdOrNull() is not int userId)
            return new JsonResult(new { success = false }) { StatusCode = 401 };
        if (!await OwnsActiveDraftAsync(request.DraftSessionId, userId))
            return new JsonResult(new { success = false, error = _localizer["Calendar_Error_DraftInactive"].Value }) { StatusCode = 409 };
        await _draftDutyService.StageDutyAssignAsync(request.DraftSessionId, request.DutyTypeValue, request.Date, request.UserId);
        return new JsonResult(new { success = true, draft = true });
    }

    public async Task<IActionResult> OnPostDraftDutyClearAsync([FromBody] DraftDutyRequest request)
    {
        if (CurrentUserIdOrNull() is not int userId)
            return new JsonResult(new { success = false }) { StatusCode = 401 };
        if (!await OwnsActiveDraftAsync(request.DraftSessionId, userId))
            return new JsonResult(new { success = false, error = _localizer["Calendar_Error_DraftInactive"].Value }) { StatusCode = 409 };
        await _draftDutyService.StageDutyClearAsync(request.DraftSessionId, request.DutyTypeValue, request.Date, request.UserId);
        return new JsonResult(new { success = true, draft = true });
    }

    public async Task<IActionResult> OnPostCommitDraftAsync([FromBody] DraftActionRequest request)
    {
        if (CurrentUserIdOrNull() is not int userId)
            return new JsonResult(new { success = false }) { StatusCode = 401 };
        if (!await OwnsActiveDraftAsync(request.DraftSessionId, userId))
            return new JsonResult(new { success = false, error = _localizer["Calendar_Error_DraftInactive"].Value }) { StatusCode = 409 };

        var result = await _draftLifecycle.CommitAsync(request.DraftSessionId, userId);
        // Per-cell policy (Spec F §5): the session commits; some cells may be Skipped (drifted) or Unauthorized
        // (grant revoked), and per-user hard errors surface as issues. The UI renders this per-cell report.
        return new JsonResult(new
        {
            success = result.Committed,
            applied = result.Applied.Select(c => new { rowId = c.RowId, date = c.WorkDate.ToString("yyyy-MM-dd"), label = c.Label }),
            appliedCount = result.Applied.Count,
            skipped = result.Skipped.Select(c => new { rowId = c.RowId, date = c.WorkDate.ToString("yyyy-MM-dd"), label = c.Label }),
            unauthorized = result.Unauthorized.Select(c => new { rowId = c.RowId, date = c.WorkDate.ToString("yyyy-MM-dd"), label = c.Label }),
            issues = result.ValidationIssues.Select(i => new { rowId = i.RowId, date = i.WorkDate.ToString("yyyy-MM-dd"), i.UserId, i.Message }),
            notifiedGroups = result.NotifiedGroups
        });
    }

    public async Task<IActionResult> OnPostDiscardDraftAsync([FromBody] DraftActionRequest request)
    {
        if (CurrentUserIdOrNull() is not int userId)
            return new JsonResult(new { success = false }) { StatusCode = 401 };
        // Allow discarding any of your OWN sessions (active or stale), not others'.
        var owns = await _db.DraftSessions.AnyAsync(d => d.Id == request.DraftSessionId && d.OwnerUserId == userId);
        if (!owns)
            return new JsonResult(new { success = false }) { StatusCode = 403 };
        await _draftLifecycle.DiscardAsync(request.DraftSessionId, userId);
        return new JsonResult(new { success = true });
    }

    public class EnterOnCallDraftRequest
    {
        public int? AreaId { get; set; }
        public DateOnly WeekStart { get; set; }
        public DateOnly WeekEnd { get; set; }
    }

    public class DraftActionRequest { public int DraftSessionId { get; set; } }

    public class DraftDutyRequest
    {
        public int DraftSessionId { get; set; }
        public int DutyTypeValue { get; set; }
        public DateOnly Date { get; set; }
        public int UserId { get; set; }
    }
}
