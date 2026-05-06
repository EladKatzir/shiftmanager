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

    public OnCallModel(
        AppDbContext db,
        IOnDutyService onDutyService,
        IGrantService grantService,
        ICompanyContext companyContext,
        IStringLocalizer<SharedResources> localizer,
        ILogger<OnCallModel> logger,
        ICalendarTextEntryService textEntryService,
        IJusticeService justiceService)
    {
        _db = db;
        _onDutyService = onDutyService;
        _grantService = grantService;
        _companyContext = companyContext;
        _localizer = localizer;
        _logger = logger;
        _textEntryService = textEntryService;
        _justiceService = justiceService;
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

    // Page properties
    public ExcelCalendarTableViewModel CalendarData { get; set; } = new();
    public bool CanEdit { get; set; }
    // See Shifts.cshtml.cs for rationale: quick-entry toggle visible to any user with WriteOverviewNotes.
    public bool CanWriteNote { get; set; }
    public List<Area> AvailableAreas { get; set; } = new();
    public Area? SelectedArea { get; set; }
    public List<DutyTypeInfo> DutyTypes { get; set; } = new();
    public int CurrentUserId { get; set; }
    public List<AppUser> Users { get; set; } = new();

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
                Name = Thread.CurrentThread.CurrentUICulture.Name.StartsWith("he")
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
            Groups = null // No grouping for duty types
        };
    }

    private Dictionary<DateOnly, ExcelCalendarCell> BuildCellsForDutyType(
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
                    ? string.Format(CultureInfo.CurrentCulture, _localizer["OnDuty_AssignedByTooltip"].Value, o.Creator.DisplayName, o.CreatedAt.ToString("d"))
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
                periodStart = view.Query.PeriodStart.ToString("yyyy-MM-dd"),
                periodEnd = view.Query.PeriodEnd.ToString("yyyy-MM-dd")
            },
            spreadIndex = view.SpreadIndex,
            spreadSeverity = view.SpreadSeverity,
            spreadLabel = _localizer[view.SpreadLabelKey].Value,
            mostOver = view.MostOver is null ? null : new { name = view.MostOver.Name, deviationPercent = view.MostOver.DeviationPercent },
            mostUnder = view.MostUnder is null ? null : new { name = view.MostUnder.Name, deviationPercent = view.MostUnder.DeviationPercent },
            rows = view.Rows.Select(r => new { id = r.Id, name = r.Name, actual = r.Actual, expected = r.Expected, deviationPercent = r.DeviationPercent, band = r.Band.ToString() }),
            maxRibbonValue = view.MaxRibbonValue,
            whereToFocus = view.WhereToFocus.Select(h => new { kind = h.Kind, shiftInstanceId = h.ShiftInstanceId, date = h.Date.ToString("yyyy-MM-dd"), label = h.Label, deficit = h.Deficit, companyId = h.CompanyId, jobTypeId = h.JobTypeId, dutyTypeValue = h.DutyTypeValue, userId = h.UserId, moleculeId = (int?)null }),
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
            date = date.ToString("yyyy-MM-dd"),
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
}
