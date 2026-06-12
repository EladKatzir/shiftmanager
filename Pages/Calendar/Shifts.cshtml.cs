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
using System.Globalization;
using System.Security.Claims;

namespace ShiftManager.Pages.Calendar;

/// <summary>
/// Excel-style Shifts Calendar page - Primary deliverable for Excel Calendars feature.
/// Supports shift-based and user-based view modes with molecule/job type filtering.
/// </summary>
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires [Authorize];
// shift types are scoped by Molecule/JobType; data re-scoped via scope switcher
[Authorize]
public class ShiftsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IShiftCalendarService _calendarService;
    private readonly IGrantService _grantService;
    private readonly ICompanyContext _companyContext;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly ICompanyLocalizationService _companyLocalizationService;
    private readonly ITenantResolver _tenantResolver;
    private readonly ILogger<ShiftsModel> _logger;
    private readonly IJobTypeService _jobTypeService;
    private readonly ITraineeService _traineeService;
    private readonly IChoreTypeService _choreTypeService;
    private readonly ICalendarTextEntryService _textEntryService;
    private readonly IJusticeService _justiceService;
    private readonly IDistributionListService _distributionListService;
    private readonly IShiftCategoryService _categoryService;
    private readonly IDraftModeService _draftService;

    public ShiftsModel(
        AppDbContext db,
        IShiftCalendarService calendarService,
        IGrantService grantService,
        ICompanyContext companyContext,
        IStringLocalizer<SharedResources> localizer,
        ICompanyLocalizationService companyLocalizationService,
        ITenantResolver tenantResolver,
        ILogger<ShiftsModel> logger,
        IJobTypeService jobTypeService,
        ITraineeService traineeService,
        IChoreTypeService choreTypeService,
        ICalendarTextEntryService textEntryService,
        IJusticeService justiceService,
        IDistributionListService distributionListService,
        IShiftCategoryService categoryService,
        IDraftModeService draftService)
    {
        _db = db;
        _calendarService = calendarService;
        _grantService = grantService;
        _companyContext = companyContext;
        _localizer = localizer;
        _companyLocalizationService = companyLocalizationService;
        _tenantResolver = tenantResolver;
        _logger = logger;
        _jobTypeService = jobTypeService;
        _traineeService = traineeService;
        _choreTypeService = choreTypeService;
        _textEntryService = textEntryService;
        _justiceService = justiceService;
        _distributionListService = distributionListService;
        _categoryService = categoryService;
        _draftService = draftService;
    }

    // Query parameters
    [BindProperty(SupportsGet = true)]
    public int? MoleculeId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? JobTypeId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Start { get; set; }

    [BindProperty(SupportsGet = true)]
    public string ViewMode { get; set; } = "week"; // week, 2weeks, month

    [BindProperty(SupportsGet = true)]
    public string Mode { get; set; } = "shift"; // shift, user

    [BindProperty(SupportsGet = true)]
    public bool CapacityMode { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool JustMine { get; set; }

    /// <summary>When true, the by-shift/by-user calendar renders the viewer's private draft overlay (Epic 4).</summary>
    [BindProperty(SupportsGet = true)]
    public bool DraftMode { get; set; }

    /// <summary>The active draft session id for the current viewer+molecule+week (null when not drafting).</summary>
    public int? ActiveDraftId { get; set; }

    /// <summary>
    /// CSV of selected distribution-list IDs (e.g. "1,2,3"). When non-empty, the by-user view is filtered
    /// to those lists' members and grouped into one collapsible section per list. Persists across navigation.
    /// </summary>
    [BindProperty(SupportsGet = true)]
    public string? DistributionListIds { get; set; }

    /// <summary>Parsed, de-duplicated selected distribution-list IDs (defensive int.TryParse per item).</summary>
    public List<int> SelectedDistributionListIds =>
        string.IsNullOrWhiteSpace(DistributionListIds)
            ? new List<int>()
            : DistributionListIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var v) ? v : (int?)null)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .Distinct()
                .ToList();

    // Page properties
    public ExcelCalendarTableViewModel CalendarData { get; set; } = new();
    public bool CanEdit { get; set; }
    // Grant NameKeys that would unlock editing when the calendar is read-only; empty when CanEdit.
    // Surfaced by the read-only banner's "?" help button so the user knows which grant to request.
    public List<string> RequiredGrantNameKeys { get; set; } = new();
    // Controls visibility of the quick-entry toggle button. True if user can assign (CanEdit) OR has
    // WriteOverviewNotes grant (company-scoped, every role has it). Lets non-assigning roles type
    // free-text notes on calendar cells. Slash commands still require CanEdit via data-can-assign.
    public bool CanWriteNote { get; set; }
    // Distribution lists for the by-user "Lists" control. Filtering is open to all; CanManageLists gates the "+".
    public bool CanManageLists { get; set; }
    public List<DistributionListSummary> AvailableDistributionLists { get; set; } = new();
    public List<Molecule> AvailableMolecules { get; set; } = new();
    public List<JobType> AvailableJobTypes { get; set; } = new();
    public Molecule? SelectedMolecule { get; set; }
    public JobType? SelectedJobType { get; set; }
    public int CurrentUserId { get; set; }
    public List<AppUser> Users { get; set; } = new();
    public List<AppUser> Trainees { get; set; } = new();
    public List<ShiftType> ShiftTypes { get; set; } = new();
    public Dictionary<int, string> LocalizedShiftTypeNames { get; set; } = new();
    public List<ChoreType> ChoreTypes { get; set; } = new();
    public List<OnDutyTypeConfig> DutyTypes { get; set; } = new();

    public bool IsTechMolecule { get; set; }

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

        // Get user's job type
        var currentUser = await _db.Users
            .Include(u => u.JobType)
            .FirstOrDefaultAsync(u => u.Id == currentUserId);

        // Set defaults for MoleculeId and JobTypeId
        if (!MoleculeId.HasValue)
        {
            MoleculeId = userCompany.MoleculeId;
        }

        if (!JobTypeId.HasValue && currentUser?.JobTypeId != null)
        {
            JobTypeId = currentUser.JobTypeId;
        }

        // Load available molecules (user has access to via grants or their own)
        await LoadAvailableMoleculesAsync(currentUserId, userCompany.MoleculeId.Value);

        // Validate selected molecule is accessible
        if (!AvailableMolecules.Any(m => m.Id == MoleculeId))
        {
            MoleculeId = userCompany.MoleculeId;
        }

        SelectedMolecule = AvailableMolecules.FirstOrDefault(m => m.Id == MoleculeId);
        IsTechMolecule = SelectedMolecule?.Type == MoleculeType.Tech;

        // Tech molecules don't use JobType for shift filtering — their shift types have JobTypeId = null.
        // Force null to prevent the user's personal JobTypeId (e.g., Hakam) from filtering out Tech shifts.
        if (IsTechMolecule)
        {
            JobTypeId = null;
        }

        // Load available job types for selected molecule
        await LoadAvailableJobTypesAsync(SelectedMolecule?.Id);

        // Validate selected job type (Workforce only — Tech already set to null above)
        if (!IsTechMolecule && (!JobTypeId.HasValue || !AvailableJobTypes.Any(jt => jt.Id == JobTypeId)))
        {
            JobTypeId = AvailableJobTypes.FirstOrDefault()?.Id;
        }

        SelectedJobType = AvailableJobTypes.FirstOrDefault(jt => jt.Id == JobTypeId);

        // Calculate date range
        CalculateDateRange();

        // Check edit permission scoped to the selected molecule and job type
        CanEdit = await _grantService.HasGrantWithScopeAsync(currentUserId, "AssignAlhutShifts", moleculeId: MoleculeId, jobTypeId: JobTypeId) ||
                  await _grantService.HasGrantWithScopeAsync(currentUserId, "AssignTextShifts", moleculeId: MoleculeId, jobTypeId: JobTypeId) ||
                  await _grantService.HasGrantWithScopeAsync(currentUserId, "AssignBRShifts", moleculeId: MoleculeId, jobTypeId: JobTypeId) ||
                  await _grantService.HasGrantWithScopeAsync(currentUserId, "AssignTechShifts", moleculeId: MoleculeId, jobTypeId: JobTypeId);

        // Note-writing is broader than assignment: any user with WriteOverviewNotes (every role has it)
        // can type free-text on calendar cells. Check is unscoped — grant 110 is company-wide by default.
        CanWriteNote = CanEdit || await _grantService.HasGrantAsync(currentUserId, "WriteOverviewNotes");

        // When read-only, resolve which assignment grant(s) the user is missing so the calendar's
        // read-only banner can tell them exactly what to request from an administrator.
        RequiredGrantNameKeys = CanEdit
            ? new List<string>()
            : await _grantService.GetGrantNameKeysAsync(
                "AssignAlhutShifts", "AssignTextShifts", "AssignBRShifts", "AssignTechShifts");

        // Distribution lists for the by-user "Lists" control. Filtering by a list needs no grant (open to all
        // viewers); CanManageLists (scoped to this molecule) gates the "+" manage button.
        if (MoleculeId.HasValue)
        {
            AvailableDistributionLists = await _distributionListService.GetListsForMoleculeAsync(MoleculeId.Value);
            CanManageLists = await _grantService.HasGrantWithScopeAsync(currentUserId, "ManageDistributionLists", moleculeId: MoleculeId);
        }

        // Build calendar data based on mode
        // JobTypeId may be null for Tech molecules — service handles nullable jobTypeId
        if (MoleculeId.HasValue)
        {
            if (Mode == "user")
            {
                await BuildUserBasedCalendarAsync(MoleculeId.Value, JobTypeId);
            }
            else
            {
                await BuildShiftBasedCalendarAsync(MoleculeId.Value, JobTypeId);
            }
        }

        // Expose users for bottom-sheet dropdown (same query as BuildUserBasedCalendarAsync)
        if (MoleculeId.HasValue)
        {
            Users = await _calendarService.GetUsersForCalendarAsync(MoleculeId.Value, JobTypeId);
        }

        // Load trainees for trainee assignment dropdown
        if (companyId.HasValue)
        {
            Trainees = await _traineeService.GetCompanyTraineesAsync(companyId.Value);
        }

        // Load chore types and duty types for Quick Entry autocomplete
        if (MoleculeId.HasValue)
        {
            ChoreTypes = await _choreTypeService.GetChoreTypesForMoleculeAsync(MoleculeId.Value);
        }
        DutyTypes = await _db.OnDutyTypeConfigs
            .Where(d => d.IsActive)
            .OrderBy(d => d.TypeValue)
            .ToListAsync();

        _logger.LogInformation(
            "Shifts calendar loaded for User {UserId}, Molecule {MoleculeId}, JobType {JobTypeId}, Mode {Mode}, ViewMode {ViewMode}",
            currentUserId, MoleculeId, JobTypeId, Mode, ViewMode);

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
            await _grantService.GetAccessibleMoleculeIdsForGrantAsync(userId, "ViewShifts"));

        // Always include user's own molecule as fallback
        moleculeIds.Add(userMoleculeId);

        AvailableMolecules = await _db.Molecules
            .Where(m => moleculeIds.Contains(m.Id) && m.IsActive)
            .OrderBy(m => m.DisplayName)
            .ToListAsync();
    }

    private async Task LoadAvailableJobTypesAsync(int? moleculeId)
    {
        if (!moleculeId.HasValue)
        {
            AvailableJobTypes = new List<JobType>();
            return;
        }

        AvailableJobTypes = await _jobTypeService.GetJobTypesForMoleculeAsync(moleculeId.Value);
    }

    private async Task BuildShiftBasedCalendarAsync(int moleculeId, int? jobTypeId)
    {
        // Resolve areaId for area-scoped shift inclusion
        var areaId = await _db.Molecules
            .Where(m => m.Id == moleculeId)
            .Select(m => m.AreaId)
            .FirstOrDefaultAsync();

        // Get shift types: molecule-scoped + area-scoped overlay
        // No query filter on ShiftType — scope-based visibility now
        var shiftTypeQuery = _db.ShiftTypes
            .Where(st =>
                st.MoleculeId == moleculeId ||
                (st.Scope == Models.Support.ShiftScope.Area && st.AreaId == areaId));

        if (jobTypeId.HasValue)
            // Include shifts matching this JobType OR null JobType (shared/area-generic shifts)
            shiftTypeQuery = shiftTypeQuery.Where(st => st.JobTypeId == jobTypeId.Value || st.JobTypeId == null);
        else
            shiftTypeQuery = shiftTypeQuery.Where(st => st.JobTypeId == null);

        var shiftTypes = (await shiftTypeQuery
            .OrderBy(st => st.Start)
            .ToListAsync())
            .OrderBy(st => st.Start)
            .ThenBy(st => st.NameEn ?? st.Key)
            .ToList();

        // Expose shift types for user-mode bottom-sheet dropdown
        ShiftTypes = shiftTypes;

        // Get shift instances and assignments
        var instances = await _calendarService.GetShiftInstancesAsync(moleculeId, jobTypeId, StartDate, EndDate);
        var assignments = await _calendarService.GetAssignmentsAsync(moleculeId, jobTypeId, StartDate, EndDate);
        // Draft Mode: overlay the viewer's private staged changes onto the live assignments for rendering.
        assignments = await ApplyDraftOverlayAsync(assignments, moleculeId, jobTypeId);

        // Get text entries + overview notes for overlay badges in shift-based view (cross-company via IgnoreQueryFilters)
        var assignedUserIds = assignments.Where(a => a.UserId.HasValue).Select(a => a.UserId!.Value).Distinct();
        var allEntriesWithType = await _textEntryService.GetForUsersAndDateRangeWithTypeAsync(assignedUserIds, StartDate, EndDate);
        // Split into text entries (for existing 📝 badge) and overview notes (for new 📋 badge)
        var textEntries = allEntriesWithType.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value
                .Where(e => e.EntryType == CalendarTextEntryType.QuickEntry)
                .Select(e => (e.Id, e.Text))
                .ToList())
            .Where(kvp => kvp.Value.Count > 0)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        // SECURITY: Only show overview notes from the viewer's own company (notes are company-scoped)
        var viewerCompanyId = _tenantResolver.GetCurrentTenantId();
        var overviewNotes = allEntriesWithType.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value
                .Where(e => e.EntryType == CalendarTextEntryType.OverviewNote && e.CompanyId == viewerCompanyId)
                .Select(e => e.Text)
                .FirstOrDefault())
            .Where(kvp => kvp.Value != null)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value!);

        // Look up company names for company-scoped shift types (molecule mode shows cross-company shifts)
        var companyIds = shiftTypes
            .Where(st => st.CompanyId.HasValue)
            .Select(st => st.CompanyId!.Value)
            .Distinct()
            .ToList();
        var companyNames = companyIds.Count > 0
            ? (await _db.Companies.IgnoreQueryFilters()
                .Where(c => companyIds.Contains(c.Id))
                .ToListAsync())
                .ToDictionary(c => c.Id, c => c.LocalizedName)
            : new Dictionary<int, string>();

        // Build rows - one per shift type
        var rows = new List<ExcelCalendarRow>();
        var companyId = _tenantResolver.GetCurrentTenantId();
        var currentCulture = System.Globalization.CultureInfo.CurrentUICulture.Name;
        var localizedNames = new Dictionary<int, string>();
        foreach (var shiftType in shiftTypes)
        {
            var localizedName = await _companyLocalizationService.ResolveShiftTypeNameAsync(
                shiftType, companyId, currentCulture);
            localizedNames[shiftType.Id] = localizedName;

            var row = new ExcelCalendarRow
            {
                Id = $"shift-{shiftType.Id}",
                Label = FormattableString.Invariant($"{localizedName} ({shiftType.Start:HH:mm}-{shiftType.End:HH:mm})"),
                Color = shiftType.RowColor,
                CompanyName = shiftType.CompanyId.HasValue ? companyNames.GetValueOrDefault(shiftType.CompanyId.Value) : null,
                // Functional category grouping (e.g. "Yekev"); null = uncategorized.
                GroupId = shiftType.CategoryId.HasValue ? $"category-{shiftType.CategoryId.Value}" : null
            };

            // Build cells for each date
            row.Cells = BuildCellsForShiftType(shiftType.Id, instances, assignments, textEntries, overviewNotes);
            rows.Add(row);
        }
        LocalizedShiftTypeNames = localizedNames;

        // Group the by-shift view by category — but only when at least one shift type is actually
        // categorized, so molecules not yet using categories keep the flat layout.
        var shiftGroups = await BuildShiftCategoryGroupsAsync(moleculeId, rows);

        CalendarData = new ExcelCalendarTableViewModel
        {
            StartDate = StartDate,
            EndDate = EndDate,
            ViewMode = ViewMode,
            IsReadOnly = !CanEdit,
            CalendarType = "shifts",
            Rows = rows,
            Groups = shiftGroups,
            RequiredGrantNameKeys = RequiredGrantNameKeys
        };
        CalendarData.RowMode = "Shifts";
        CalendarData.DraftSessionId = ActiveDraftId;
        // +1 for the <thead> column-header row so aria-rowcount matches ARIA 1.2 §6.6.4
        // (rowcount includes ALL <tr> elements, not just <tbody> rows).
        CalendarData.TotalRows = CalendarData.Rows.Count + (CalendarData.Groups?.Count ?? 0) + 1;
        CalendarData.RowOrderContextKey = $"shifts:{moleculeId}:{jobTypeId}:shift";
    }

    private async Task BuildUserBasedCalendarAsync(int moleculeId, int? jobTypeId)
    {
        // Resolve areaId for area-scoped shift inclusion
        var userAreaId = await _db.Molecules
            .Where(m => m.Id == moleculeId)
            .Select(m => m.AreaId)
            .FirstOrDefaultAsync();

        // Load shift types: molecule-scoped + area-scoped overlay
        var shiftTypeQuery = _db.ShiftTypes
            .Where(st =>
                st.MoleculeId == moleculeId ||
                (st.Scope == Models.Support.ShiftScope.Area && st.AreaId == userAreaId));

        if (jobTypeId.HasValue)
            shiftTypeQuery = shiftTypeQuery.Where(st => st.JobTypeId == jobTypeId.Value || st.JobTypeId == null);
        else
            shiftTypeQuery = shiftTypeQuery.Where(st => st.JobTypeId == null);

        ShiftTypes = (await shiftTypeQuery
            .OrderBy(st => st.Start)
            .ToListAsync())
            .OrderBy(st => st.Start)
            .ThenBy(st => st.NameEn ?? st.Key)
            .ToList();

        // Get users for this molecule/job type
        var users = await _calendarService.GetUsersForCalendarAsync(moleculeId, jobTypeId);

        // Filter to just current user if "Just Mine" is enabled
        if (JustMine)
        {
            users = users.Where(u => u.Id == CurrentUserId).ToList();
        }

        // Get shift instances and assignments
        var instances = await _calendarService.GetShiftInstancesAsync(moleculeId, jobTypeId, StartDate, EndDate);
        var assignments = await _calendarService.GetAssignmentsAsync(moleculeId, jobTypeId, StartDate, EndDate);
        // Draft Mode: overlay the viewer's private staged changes onto the live assignments for rendering.
        assignments = await ApplyDraftOverlayAsync(assignments, moleculeId, jobTypeId);

        // Get overlays (vacation, chores, on-duty)
        var overlays = await _calendarService.GetOverlaysAsync(moleculeId, StartDate, EndDate);

        // Get text entries + overview notes for user-mode cells (cross-company via IgnoreQueryFilters)
        var userIds = users.Select(u => u.Id);
        var allUserEntriesWithType = await _textEntryService.GetForUsersAndDateRangeWithTypeAsync(userIds, StartDate, EndDate);
        // Split into text entries (chips with ×) and overview notes (read-only 📋 chips)
        var textEntries = allUserEntriesWithType.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value
                .Where(e => e.EntryType == CalendarTextEntryType.QuickEntry)
                .Select(e => (e.Id, e.Text))
                .ToList())
            .Where(kvp => kvp.Value.Count > 0)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        // SECURITY: Only show overview notes from the viewer's own company (notes are company-scoped)
        var companyId = _tenantResolver.GetCurrentTenantId();
        var overviewNotes = allUserEntriesWithType.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value
                .Where(e => e.EntryType == CalendarTextEntryType.OverviewNote && e.CompanyId == companyId)
                .Select(e => e.Text)
                .FirstOrDefault())
            .Where(kvp => kvp.Value != null)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value!);

        // Pre-resolve localized names for all shift types (used in user-mode cells)
        var culture = System.Globalization.CultureInfo.CurrentUICulture.Name;
        var localizedShiftNames = new Dictionary<int, string>();
        foreach (var st in ShiftTypes)
        {
            localizedShiftNames[st.Id] = await _companyLocalizationService.ResolveShiftTypeNameAsync(st, companyId, culture);
        }
        LocalizedShiftTypeNames = localizedShiftNames;

        // Build rows - one per user
        var rows = new List<ExcelCalendarRow>();
        List<ExcelCalendarGroup>? groups = null;

        if (SelectedDistributionListIds.Any())
        {
            // Distribution lists are a SEPARATE, user-driven arrangement tool. When lists are selected they
            // override category grouping: one collapsible section per selected list (cross-company), + "Other".
            (rows, groups) = await BuildListGroupedRowsAsync(
                users, SelectedDistributionListIds, moleculeId, instances, assignments, overlays, localizedShiftNames, textEntries, overviewNotes);
        }
        else
        {
            // Default (all molecule types): group by ShiftCategory. Users with DoesShifts=true appear under
            // EACH of their categories (mirrored rows); everyone else falls under their Company header. This
            // is the single grouping engine that superseded the old PrimaryShiftType/tech + flat paths.
            (rows, groups) = await BuildCategoryGroupedRowsAsync(
                users, moleculeId, instances, assignments, overlays, localizedShiftNames, textEntries, overviewNotes);
        }

        CalendarData = new ExcelCalendarTableViewModel
        {
            StartDate = StartDate,
            EndDate = EndDate,
            ViewMode = ViewMode,
            IsReadOnly = !CanEdit,
            CalendarType = "shifts",
            Rows = rows,
            Groups = groups,
            RequiredGrantNameKeys = RequiredGrantNameKeys
        };
        // BuildUserBasedCalendarAsync only runs when Mode == "user" (see OnGet routing).
        CalendarData.RowMode = "Users";
        CalendarData.DraftSessionId = ActiveDraftId;
        // +1 for the <thead> column-header row (ARIA 1.2 §6.6.4).
        CalendarData.TotalRows = CalendarData.Rows.Count + (CalendarData.Groups?.Count ?? 0) + 1;
        CalendarData.RowOrderContextKey = $"shifts:{moleculeId}:{jobTypeId}:user";
    }

    /// <summary>
    /// Builds collapsible sections for the by-user calendar — one section per selected distribution list,
    /// filtered to that list's members present in the loaded (molecule + jobType) <paramref name="users"/> set.
    /// A user in multiple selected lists appears once per section; the duplicate data-row-id is intentional and
    /// safe (the calendar resolves interactions from the data-row-id attribute on the clicked cell, not a DOM id).
    /// Reuses the same ExcelCalendarGroup rendering as the by-company / by-shift-type sections.
    /// </summary>
    private async Task<(List<ExcelCalendarRow> rows, List<ExcelCalendarGroup> groups)> BuildListGroupedRowsAsync(
        List<AppUser> users,
        List<int> selectedListIds,
        int moleculeId,
        List<ShiftInstance> instances,
        List<ShiftAssignment> assignments,
        Dictionary<(int UserId, DateOnly Date), FyiOverlayData> overlays,
        Dictionary<int, string> localizedShiftNames,
        Dictionary<(int UserId, DateOnly Date), List<(int Id, string Text)>> textEntries,
        Dictionary<(int UserId, DateOnly Date), string> overviewNotes)
    {
        var rows = new List<ExcelCalendarRow>();
        var groups = new List<ExcelCalendarGroup>();

        // Lists with their member IDs (molecule-scoped, ordered by name). Cross-company members are expected.
        var lists = await _distributionListService.GetListsWithMembersAsync(selectedListIds, moleculeId);
        if (lists.Count == 0)
            return (rows, groups);

        // Only members present in the loaded user set are shown (respects active + jobType filtering upstream).
        var usersById = users.ToDictionary(u => u.Id);

        // Company names for the per-row company badge (lists span companies within the molecule).
        // SECURITY-AUDITED: SAFE — scoped to CompanyIds of the molecule-derived user set.
        var companyIds = users.Select(u => u.CompanyId).Distinct().ToList();
        var companyLookup = await _db.Companies
            .IgnoreQueryFilters()
            .Where(c => companyIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.LocalizedName);

        // Tracks every user already placed under a selected list, so the trailing "Other" group can hold
        // everyone else. The view is a PARTITION of all loaded users (selected lists + Other), not a filter
        // down to list members — selecting lists divides the calendar, it never hides non-members. (Issue: lists)
        var placedUserIds = new HashSet<int>();

        int sortOrder = 0;
        foreach (var list in lists)
        {
            var groupId = $"dl-{list.Id}";

            // Members present in this view, ordered by display name (consistent with GetUsersForCalendarAsync).
            var members = list.MemberUserIds
                .Where(usersById.ContainsKey)
                .Select(id => usersById[id])
                .OrderBy(u => u.DisplayName)
                .ToList();

            foreach (var m in members)
                placedUserIds.Add(m.Id);

            groups.Add(new ExcelCalendarGroup
            {
                Id = groupId,
                Name = list.Name,
                SortOrder = sortOrder++,
                MemberCount = members.Count
            });

            foreach (var user in members)
            {
                // Keep Id = "user-{id}" so calendar interactions resolve the user correctly even when the same
                // user appears under multiple selected lists (duplicate data-row-id is safe — attribute, not DOM id).
                var row = new ExcelCalendarRow
                {
                    Id = $"user-{user.Id}",
                    Label = user.DisplayName,
                    GroupId = groupId,
                    CompanyName = companyLookup.GetValueOrDefault(user.CompanyId)
                };
                row.Cells = BuildCellsForUser(user.Id, instances, assignments, overlays, localizedShiftNames, textEntries, overviewNotes);

                var userShiftWindows = assignments
                    .Where(a => a.UserId == user.Id && a.ShiftInstance.WorkDate >= StartDate && a.ShiftInstance.WorkDate <= EndDate && !a.ShiftInstance.ShiftType.IsHome && !a.ShiftInstance.ShiftType.IsOffline)
                    .Select(a => TimeHelpers.GetShiftWindow(a.ShiftInstance.ShiftType, a.ShiftInstance.WorkDate))
                    .OrderBy(w => w.start)
                    .ToList();
                row.WeeklyHours = TimeHelpers.MergeAndSumHours(userShiftWindows);
                rows.Add(row);
            }
        }

        // Trailing "Other" section: every loaded user not in any selected list, so the calendar shows ALL
        // users divided by lists rather than filtering down to list members. Omitted when everyone is listed.
        var otherUsers = users
            .Where(u => !placedUserIds.Contains(u.Id))
            .OrderBy(u => u.DisplayName)
            .ToList();
        if (otherUsers.Count > 0)
        {
            const string otherGroupId = "dl-other";
            groups.Add(new ExcelCalendarGroup
            {
                Id = otherGroupId,
                Name = _localizer["DistributionList_OtherGroup"],
                SortOrder = sortOrder++,
                MemberCount = otherUsers.Count
            });

            foreach (var user in otherUsers)
            {
                var row = new ExcelCalendarRow
                {
                    Id = $"user-{user.Id}",
                    Label = user.DisplayName,
                    GroupId = otherGroupId,
                    CompanyName = companyLookup.GetValueOrDefault(user.CompanyId)
                };
                row.Cells = BuildCellsForUser(user.Id, instances, assignments, overlays, localizedShiftNames, textEntries, overviewNotes);

                var userShiftWindows = assignments
                    .Where(a => a.UserId == user.Id && a.ShiftInstance.WorkDate >= StartDate && a.ShiftInstance.WorkDate <= EndDate && !a.ShiftInstance.ShiftType.IsHome && !a.ShiftInstance.ShiftType.IsOffline)
                    .Select(a => TimeHelpers.GetShiftWindow(a.ShiftInstance.ShiftType, a.ShiftInstance.WorkDate))
                    .OrderBy(w => w.start)
                    .ToList();
                row.WeeklyHours = TimeHelpers.MergeAndSumHours(userShiftWindows);
                rows.Add(row);
            }
        }

        return (rows, groups);
    }

    /// <summary>
    /// Builds a single by-user calendar row (cells + weekly hours), shared by every by-user grouping
    /// strategy. <paramref name="groupId"/> places it under a collapsible section; <paramref name="companyName"/>
    /// drives the per-row company badge (null to suppress, e.g. inside a company group where it is redundant).
    /// </summary>
    private ExcelCalendarRow BuildUserRow(
        AppUser user,
        string? groupId,
        string? companyName,
        string? subLabel,
        List<ShiftInstance> instances,
        List<ShiftAssignment> assignments,
        Dictionary<(int UserId, DateOnly Date), FyiOverlayData> overlays,
        Dictionary<int, string> localizedShiftNames,
        Dictionary<(int UserId, DateOnly Date), List<(int Id, string Text)>> textEntries,
        Dictionary<(int UserId, DateOnly Date), string> overviewNotes)
    {
        var row = new ExcelCalendarRow
        {
            Id = $"user-{user.Id}",
            Label = user.DisplayName,
            SubLabel = subLabel,
            GroupId = groupId,
            CompanyName = companyName
        };
        row.Cells = BuildCellsForUser(user.Id, instances, assignments, overlays, localizedShiftNames, textEntries, overviewNotes);

        var userShiftWindows = assignments
            .Where(a => a.UserId == user.Id && a.ShiftInstance.WorkDate >= StartDate && a.ShiftInstance.WorkDate <= EndDate && !a.ShiftInstance.ShiftType.IsHome && !a.ShiftInstance.ShiftType.IsOffline)
            .Select(a => TimeHelpers.GetShiftWindow(a.ShiftInstance.ShiftType, a.ShiftInstance.WorkDate))
            .OrderBy(w => w.start)
            .ToList();
        row.WeeklyHours = TimeHelpers.MergeAndSumHours(userShiftWindows);
        return row;
    }

    /// <summary>
    /// Default by-user grouping (replaces the legacy PrimaryShiftType/tech + flat paths). Users with
    /// DoesShifts = true render under EACH ShiftCategory they belong to — the same user appears as a
    /// MIRRORED row under multiple category accordions (duplicate data-row-id is safe; the calendar
    /// resolves interactions from the cell's data-row-id attribute, not a DOM id). Everyone else — and
    /// any participant who is not mapped to a category — falls under their Company header.
    /// </summary>
    private async Task<(List<ExcelCalendarRow> rows, List<ExcelCalendarGroup> groups)> BuildCategoryGroupedRowsAsync(
        List<AppUser> users,
        int moleculeId,
        List<ShiftInstance> instances,
        List<ShiftAssignment> assignments,
        Dictionary<(int UserId, DateOnly Date), FyiOverlayData> overlays,
        Dictionary<int, string> localizedShiftNames,
        Dictionary<(int UserId, DateOnly Date), List<(int Id, string Text)>> textEntries,
        Dictionary<(int UserId, DateOnly Date), string> overviewNotes)
    {
        var rows = new List<ExcelCalendarRow>();
        var groups = new List<ExcelCalendarGroup>();

        // Company names (per-row badge for cross-company category members + company-group headers).
        // SECURITY-AUDITED: SAFE — scoped to the molecule-derived user set's CompanyIds.
        var companyIds = users.Select(u => u.CompanyId).Distinct().ToList();
        var companyLookup = await _db.Companies
            .IgnoreQueryFilters()
            .Where(c => companyIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.LocalizedName);

        // HomeType SubLabels (parity with the prior grouping).
        var homeTypeIds = users.Where(u => u.HomeTypeId.HasValue).Select(u => u.HomeTypeId!.Value).Distinct().ToList();
        var homeTypeNames = homeTypeIds.Count > 0
            ? await _db.HomeTypes.IgnoreQueryFilters()
                .Where(ht => homeTypeIds.Contains(ht.Id))
                .ToDictionaryAsync(ht => ht.Id, ht => ht.NameHe ?? ht.Name)
            : new Dictionary<int, string>();

        string? SubLabel(AppUser u) => u.HomeTypeId.HasValue ? homeTypeNames.GetValueOrDefault(u.HomeTypeId.Value) : null;

        // Categories (active, ordered) + memberships restricted to participants in this view.
        var categories = await _categoryService.GetCategoriesForMoleculeAsync(moleculeId);
        var participantIds = users.Where(u => u.DoesShifts).Select(u => u.Id).ToHashSet();
        var byCategory = new Dictionary<int, HashSet<int>>();
        if (participantIds.Count > 0)
        {
            var memberships = await _db.UserShiftCategories
                .Where(m => participantIds.Contains(m.UserId))
                .Select(m => new { m.ShiftCategoryId, m.UserId })
                .ToListAsync();
            byCategory = memberships
                .GroupBy(m => m.ShiftCategoryId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.UserId).ToHashSet());
        }

        var placedParticipants = new HashSet<int>();
        int sortOrder = 0;

        // Category accordions in service order; the same participant can appear under several (mirrored rows).
        foreach (var cat in categories)
        {
            if (!byCategory.TryGetValue(cat.Id, out var memberIdSet))
                continue;
            var members = users.Where(u => memberIdSet.Contains(u.Id)).OrderBy(u => u.DisplayName).ToList();
            if (members.Count == 0)
                continue;

            var groupId = $"category-{cat.Id}";
            groups.Add(new ExcelCalendarGroup
            {
                Id = groupId,
                Name = cat.DisplayName,
                SortOrder = sortOrder++,
                Color = cat.Color,
                MemberCount = members.Count
            });
            foreach (var user in members)
            {
                placedParticipants.Add(user.Id);
                rows.Add(BuildUserRow(user, groupId, companyLookup.GetValueOrDefault(user.CompanyId), SubLabel(user),
                    instances, assignments, overlays, localizedShiftNames, textEntries, overviewNotes));
            }
        }

        // Company groups: non-participants + any participant not mapped to a category (so nobody is lost).
        var companyGrouped = users
            .Where(u => !u.DoesShifts || !placedParticipants.Contains(u.Id))
            .GroupBy(u => u.CompanyId)
            .OrderBy(g => companyLookup.GetValueOrDefault(g.Key, ""));
        foreach (var grp in companyGrouped)
        {
            var groupId = $"company-{grp.Key}";
            var members = grp.OrderBy(u => u.DisplayName).ToList();
            groups.Add(new ExcelCalendarGroup
            {
                Id = groupId,
                Name = companyLookup.GetValueOrDefault(grp.Key, $"Company #{grp.Key}"),
                SortOrder = sortOrder++,
                MemberCount = members.Count
            });
            foreach (var user in members)
            {
                // Company badge is redundant inside a company group — suppress it.
                rows.Add(BuildUserRow(user, groupId, null, SubLabel(user),
                    instances, assignments, overlays, localizedShiftNames, textEntries, overviewNotes));
            }
        }

        return (rows, groups);
    }

    /// <summary>
    /// Groups the by-shift view rows by their owning ShiftCategory. Returns null (flat layout) when no shift
    /// type in the molecule is categorized, so molecules not yet using categories are unaffected. Shift rows
    /// already carry their GroupId ("category-{id}" or null); uncategorized rows collect under an "Other" group.
    /// </summary>
    private async Task<List<ExcelCalendarGroup>?> BuildShiftCategoryGroupsAsync(int moleculeId, List<ExcelCalendarRow> rows)
    {
        var categories = await _categoryService.GetCategoriesForMoleculeAsync(moleculeId);
        if (categories.Count == 0 || rows.All(r => string.IsNullOrEmpty(r.GroupId)))
            return null;

        var groups = new List<ExcelCalendarGroup>();
        int sortOrder = 0;
        foreach (var cat in categories)
        {
            var groupId = $"category-{cat.Id}";
            var count = rows.Count(r => r.GroupId == groupId);
            if (count == 0)
                continue;
            groups.Add(new ExcelCalendarGroup
            {
                Id = groupId,
                Name = cat.DisplayName,
                SortOrder = sortOrder++,
                Color = cat.Color,
                MemberCount = count
            });
        }

        // Uncategorized shift rows → trailing "Other" group so grouped + ungrouped rows don't render mixed.
        var uncategorized = rows.Where(r => string.IsNullOrEmpty(r.GroupId)).ToList();
        if (uncategorized.Count > 0)
        {
            const string otherId = "category-other";
            foreach (var r in uncategorized)
                r.GroupId = otherId;
            groups.Add(new ExcelCalendarGroup
            {
                Id = otherId,
                Name = _localizer["Calendar_UncategorizedGroup"],
                SortOrder = sortOrder++,
                MemberCount = uncategorized.Count
            });
        }

        return groups;
    }

    /// <summary>
    /// Draft Mode (Epic 4): overlays the viewer's active draft onto the live assignments for rendering —
    /// touched cells show their STAGED assignees (synthetic, in-memory <see cref="ShiftAssignment"/>s)
    /// instead of the live ones. Sets <see cref="ActiveDraftId"/>. Returns the live list unchanged when
    /// not in draft mode or no active draft exists.
    /// </summary>
    private async Task<List<ShiftAssignment>> ApplyDraftOverlayAsync(List<ShiftAssignment> live, int moleculeId, int? jobTypeId)
    {
        if (!DraftMode)
            return live;

        var draft = await _draftService.GetActiveDraftAsync(CurrentUserId, moleculeId, jobTypeId, StartDate);
        if (draft == null)
            return live;
        ActiveDraftId = draft.Id;

        var overlay = await _draftService.GetOverlayAsync(draft.Id);
        if (overlay.Count == 0)
            return live;

        var touched = overlay.ToDictionary(o => (o.ShiftTypeId, o.WorkDate), o => o.UserIds);
        var stById = ShiftTypes.ToDictionary(s => s.Id);

        // A staged cell may reference a shift type not in this view's ShiftTypes set (e.g. company-scoped).
        // Resolve any such types from the DB so their staged chips still render in the overlay.
        var missingStIds = touched.Keys.Select(k => k.ShiftTypeId).Where(id => !stById.ContainsKey(id)).Distinct().ToList();
        if (missingStIds.Count > 0)
        {
            // SECURITY-AUDITED: SAFE — shift types are not tenant-filtered; loaded by explicit ids.
            var extra = await _db.ShiftTypes.IgnoreQueryFilters().Where(s => missingStIds.Contains(s.Id)).ToListAsync();
            foreach (var s in extra)
                stById[s.Id] = s;
        }

        // Keep live assignments for untouched cells; reuse live ShiftInstance objects where present.
        var result = live.Where(a => !touched.ContainsKey((a.ShiftInstance.ShiftTypeId, a.ShiftInstance.WorkDate))).ToList();
        var instanceByCell = live
            .GroupBy(a => (a.ShiftInstance.ShiftTypeId, a.ShiftInstance.WorkDate))
            .ToDictionary(g => g.Key, g => g.First().ShiftInstance);

        int synthId = -1;
        foreach (var (key, userIds) in touched)
        {
            if (!stById.TryGetValue(key.ShiftTypeId, out var st))
                continue;

            if (!instanceByCell.TryGetValue(key, out var instance))
            {
                instance = new ShiftInstance
                {
                    Id = synthId--,
                    ShiftTypeId = key.ShiftTypeId,
                    WorkDate = key.WorkDate,
                    ShiftType = st,
                    CompanyId = st.CompanyId ?? 0,
                    StaffingRequired = Math.Max(1, userIds.Count)
                };
            }
            else if (instance.ShiftType == null)
            {
                instance.ShiftType = st;
            }

            foreach (var uid in userIds)
            {
                result.Add(new ShiftAssignment
                {
                    Id = synthId--,
                    ShiftInstanceId = instance.Id,
                    ShiftInstance = instance,
                    UserId = uid
                });
            }
        }

        return result;
    }

    private Dictionary<DateOnly, ExcelCalendarCell> BuildCellsForShiftType(
        int shiftTypeId,
        List<ShiftInstance> instances,
        List<ShiftAssignment> assignments,
        Dictionary<(int UserId, DateOnly Date), List<(int Id, string Text)>> textEntries,
        Dictionary<(int UserId, DateOnly Date), string> overviewNotes)
    {
        var cells = new Dictionary<DateOnly, ExcelCalendarCell>();

        for (var date = StartDate; date <= EndDate; date = date.AddDays(1))
        {
            var instance = instances.FirstOrDefault(i => i.ShiftTypeId == shiftTypeId && i.WorkDate == date);
            var cell = new ExcelCalendarCell();

            if (instance != null)
            {
                var instanceAssignments = assignments
                    .Where(a => a.ShiftInstanceId == instance.Id)
                    .ToList();

                cell.Assignments = instanceAssignments
                    .Where(a => a.UserId != null)
                    .Select(a => new ExcelCalendarAssignment
                    {
                        Id = a.Id,
                        Name = a.User?.DisplayName ?? _localizer["Unassigned"].Value,
                        IsTrainee = false, // Shift-mode rows are primary employees, never trainees
                        UserId = a.UserId,
                        ShiftTypeId = a.ShiftInstance.ShiftTypeId,
                        TraineeUserId = a.TraineeUserId,
                        TraineeName = a.Trainee?.DisplayName,
                        // HOME unification (Task 22): expose source-of-truth so renderer can pick
                        // repeat (rotation) / plane (vacation) / sunrise (after) icons. Shift-mode
                        // doesn't normally show HOME (HOME is JobTypeId=null), but stay consistent.
                        IsHome = a.ShiftInstance.ShiftType?.IsHome == true,
                        ShiftStart = a.ShiftInstance.ShiftType?.Start.ToString("HH:mm", CultureInfo.InvariantCulture),
                        ShiftEnd = a.ShiftInstance.ShiftType?.End.ToString("HH:mm", CultureInfo.InvariantCulture),
                        SourceTimeOffRequestId = a.SourceTimeOffRequestId,
                        SourceTimeOffRequestType = a.SourceTimeOffRequest != null
                            ? (int?)a.SourceTimeOffRequest.Type
                            : null
                    }).ToList();

                if (CapacityMode)
                {
                    cell.Capacity = instanceAssignments.Count(a => a.UserId != null);
                    cell.DefaultCapacity = instance.StaffingRequired;
                }

                // Check if any assigned user has text entries for this date (overlay badge)
                var assignedUserIds = instanceAssignments
                    .Where(a => a.UserId != null)
                    .Select(a => a.UserId!.Value)
                    .Distinct();
                var cellTextEntryTexts = new List<string>();
                foreach (var uid in assignedUserIds)
                {
                    if (textEntries.TryGetValue((uid, date), out var entries))
                        cellTextEntryTexts.AddRange(entries.Select(e => e.Text));
                }
                if (cellTextEntryTexts.Count > 0)
                {
                    cell.Overlay ??= new ExcelCalendarOverlay();
                    cell.Overlay.HasTextEntry = true;
                    cell.Overlay.TextEntryTexts = cellTextEntryTexts;
                }

                // Check if any assigned user has overview notes (📋 badge with aggregated tooltip)
                var cellNoteTexts = new List<string>();
                foreach (var uid in assignedUserIds)
                {
                    if (overviewNotes.TryGetValue((uid, date), out var noteText))
                        cellNoteTexts.Add(noteText);
                }
                if (cellNoteTexts.Count > 0)
                {
                    cell.Overlay ??= new ExcelCalendarOverlay();
                    cell.Overlay.HasOverviewNote = true;
                    cell.Overlay.OverviewNoteText = string.Join("\n", cellNoteTexts);
                }
            }

            cells[date] = cell;
        }

        return cells;
    }

    private Dictionary<DateOnly, ExcelCalendarCell> BuildCellsForUser(
        int userId,
        List<ShiftInstance> instances,
        List<ShiftAssignment> assignments,
        Dictionary<(int UserId, DateOnly Date), FyiOverlayData> overlays,
        Dictionary<int, string> localizedShiftNames,
        Dictionary<(int UserId, DateOnly Date), List<(int Id, string Text)>> textEntries,
        Dictionary<(int UserId, DateOnly Date), string> overviewNotes)
    {
        var cells = new Dictionary<DateOnly, ExcelCalendarCell>();

        for (var date = StartDate; date <= EndDate; date = date.AddDays(1))
        {
            var cell = new ExcelCalendarCell();

            // Get user's assignments for this date
            var userAssignments = assignments
                .Where(a => (a.UserId == userId || a.TraineeUserId == userId) && a.ShiftInstance.WorkDate == date)
                .ToList();

            cell.Assignments = userAssignments.Select(a => new ExcelCalendarAssignment
            {
                Id = a.Id,
                Name = localizedShiftNames.GetValueOrDefault(a.ShiftInstance.ShiftTypeId,
                    a.ShiftInstance.ShiftType?.Name ?? _localizer["Shift"].Value),
                SubLabel = a.Note,
                Role = a.ShiftInstance.ShiftType?.RowColor,
                IsTrainee = a.TraineeUserId == userId,
                IsTraineeShift = a.IsTraineeShift,
                UserId = a.UserId,
                ShiftTypeId = a.ShiftInstance.ShiftTypeId,
                TraineeUserId = a.TraineeUserId,
                TraineeName = a.Trainee?.DisplayName,
                // HOME unification (Task 22): user-mode chips render with source icons
                // (repeat/plane/sunrise) + house icon + time range when ShiftType.IsHome.
                IsHome = a.ShiftInstance.ShiftType?.IsHome == true,
                ShiftStart = a.ShiftInstance.ShiftType?.Start.ToString("HH:mm", CultureInfo.InvariantCulture),
                ShiftEnd = a.ShiftInstance.ShiftType?.End.ToString("HH:mm", CultureInfo.InvariantCulture),
                SourceTimeOffRequestId = a.SourceTimeOffRequestId,
                SourceTimeOffRequestType = a.SourceTimeOffRequest != null
                    ? (int?)a.SourceTimeOffRequest.Type
                    : null
            }).ToList();

            // Add overlay data: render chore/duty items as non-removable assignment chips
            if (overlays.TryGetValue((userId, date), out var overlay))
            {
                // Chore items rendered as colored assignment chips (Id=0 → non-removable)
                foreach (var chore in overlay.ChoreItems)
                {
                    cell.Assignments.Add(new ExcelCalendarAssignment
                    {
                        Id = 0,
                        Name = chore.Name,
                        Role = chore.Color ?? "chore"
                    });
                }

                // On-duty items rendered as colored assignment chips
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
                    DayAtLabel = overlay.DayAtLabel,
                    HasChore = overlay.HasChore,
                    HasOnDuty = overlay.HasOnDuty,
                    OtherItems = overlay.OtherShifts
                };
            }

            // Text entries rendered as visually distinct chips (real Id enables deletion)
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

    // ===============================================================================
    // Justice analytics — in-context drawer (Phase 2)
    // ===============================================================================

    /// <summary>
    /// JSON endpoint feeding the slide-in Justice drawer on this calendar.
    /// Auto-derives scope from the page's MoleculeId / JobTypeId / Start state.
    /// Authorization: requires ViewJusticeTable grant in the requested molecule scope.
    /// </summary>
    public async Task<IActionResult> OnGetJusticeAsync(CancellationToken ct)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdClaim, out var currentUserId))
        {
            return Forbid();
        }

        if (!MoleculeId.HasValue)
        {
            return new JsonResult(new { error = "no_scope" });
        }

        // Scope-gating: confirm the user has Justice access in this molecule.
        var allowedMoleculeIds = await _grantService.GetAccessibleMoleculeIdsForGrantAsync(currentUserId, "ViewJusticeTable");
        if (!allowedMoleculeIds.Contains(MoleculeId.Value))
        {
            return Forbid();
        }

        // Period: align with the calendar's current view window (Start / End already computed in OnGetAsync,
        // but this handler runs independently. Recompute here from the same Start binding.)
        DateOnly periodStart, periodEnd;
        ResolveJusticePeriod(out periodStart, out periodEnd);

        var query = new JusticeQuery(
            Scope: JusticeScope.Molecule,
            ScopeId: MoleculeId,
            PeriodStart: periodStart,
            PeriodEnd: periodEnd,
            WorkType: JusticeWorkType.Shift,
            ExcludeExemptShifts: true,
            Level: JusticeLevel.CompaniesInMolecule);

        var view = await _justiceService.GetInContextViewAsync(query, ct);
        return new JsonResult(BuildJusticeJson(view));
    }

    private void ResolveJusticePeriod(out DateOnly start, out DateOnly end)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var anchor = today;
        if (!string.IsNullOrEmpty(Start) && DateOnly.TryParse(Start, out var parsed))
        {
            anchor = parsed;
        }
        // Use a 30-day window centered on the calendar's anchor for fairness math.
        // Keeps the drawer's "Where to focus" within a meaningful planning horizon.
        start = anchor.AddDays(-15);
        end = anchor.AddDays(15);
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
            noHolesLabel = _localizer["Justice_Panel_NoHoles"].Value
        };
    }

    /// <summary>
    /// Phase 2b — JSON endpoint that ranks candidate users to fill a specific shift hole.
    /// Accepts either:
    ///   - <c>rowId</c> + <c>date</c> (cell click — rowId is the ShiftType id), or
    ///   - <c>shiftInstanceId</c> + <c>date</c> (focus-list click — instance already known).
    /// Returns ranked candidates least-loaded-first, with hard-blocked candidates collapsed.
    /// </summary>
    public async Task<IActionResult> OnGetJusticeEligibilityAsync(int? rowId, int? shiftInstanceId, string? date, CancellationToken ct)
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

        // SECURITY-AUDITED: SAFE — instance lookup by either composite (ShiftTypeId, WorkDate)
        // or by primary key. Page-level grant gates Justice access on this molecule; the
        // instance's molecule is re-verified below before any candidate data is exposed.
        var instanceQuery = _db.ShiftInstances.IgnoreQueryFilters();
        var instance = shiftInstanceId.HasValue
            ? await instanceQuery
                .Where(si => si.Id == shiftInstanceId.Value && si.WorkDate == workDate)
                .Select(si => new { si.Id, si.CompanyId, si.ShiftTypeId })
                .FirstOrDefaultAsync(ct)
            : (rowId.HasValue
                ? await instanceQuery
                    .Where(si => si.ShiftTypeId == rowId.Value && si.WorkDate == workDate)
                    .Select(si => new { si.Id, si.CompanyId, si.ShiftTypeId })
                    .FirstOrDefaultAsync(ct)
                : null);

        if (instance is null)
            return new JsonResult(new { error = "no_instance" });

        // Defense in depth: caller has grant on MoleculeId; verify the instance's company is in
        // that molecule (ShiftInstance has no MoleculeId column — go through Company).
        var instanceMoleculeId = await _db.Companies
            .IgnoreQueryFilters()
            .Where(c => c.Id == instance.CompanyId)
            .Select(c => c.MoleculeId)
            .FirstOrDefaultAsync(ct);

        if (instanceMoleculeId != MoleculeId.Value)
            return Forbid();

        // SECURITY-AUDITED: SAFE — ShiftType lookup by unique id; only its JobTypeId is read for filtering.
        var shiftType = await _db.ShiftTypes
            .IgnoreQueryFilters()
            .Where(st => st.Id == instance.ShiftTypeId)
            .Select(st => new { st.JobTypeId })
            .FirstOrDefaultAsync(ct);

        DateOnly periodStart, periodEnd;
        ResolveJusticePeriod(out periodStart, out periodEnd);

        // Eligibility ranking uses Company-scoped UsersInCompany rows so each candidate carries a
        // per-user deviation %. Drawer's verdict view uses CompaniesInMolecule for aggregates;
        // these are different scopes on purpose — verdict is "how is the molecule doing?" while
        // ranking is "how loaded is each candidate?".
        var parentScope = new JusticeQuery(
            Scope: JusticeScope.Company,
            ScopeId: instance.CompanyId,
            PeriodStart: periodStart,
            PeriodEnd: periodEnd,
            WorkType: JusticeWorkType.Shift,
            ExcludeExemptShifts: true,
            Level: JusticeLevel.UsersInCompany);

        var hole = new HoleSelector(
            Kind: "shift",
            ShiftInstanceId: instance.Id,
            Date: workDate,
            CompanyId: instance.CompanyId,
            JobTypeId: shiftType?.JobTypeId,
            DutyTypeValue: null,
            MoleculeId: null,                    // shifts resolve molecule via Company join in BusyService
            ParentScope: parentScope);

        var result = await _justiceService.GetEligibleCandidatesAsync(hole, ct);
        return new JsonResult(BuildEligibilityJson(result, instance.Id, instance.ShiftTypeId, workDate));
    }

    /// <summary>
    /// Phase 2c — JSON endpoint that previews the spread-index impact of assigning a user to a hole.
    /// Pure in-memory recompute; never writes to the DB.
    /// </summary>
    public async Task<IActionResult> OnGetJusticePreviewAsync(int userId, int shiftInstanceId, CancellationToken ct)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdClaim, out var currentUserId))
            return Forbid();

        if (!MoleculeId.HasValue)
            return new JsonResult(new { error = "no_scope" });

        var allowedMoleculeIds = await _grantService.GetAccessibleMoleculeIdsForGrantAsync(currentUserId, "ViewJusticeTable");
        if (!allowedMoleculeIds.Contains(MoleculeId.Value))
            return Forbid();

        // SECURITY-AUDITED: SAFE — ShiftInstance lookup by unique id; molecule re-verified below.
        var instance = await _db.ShiftInstances
            .IgnoreQueryFilters()
            .Where(si => si.Id == shiftInstanceId)
            .Select(si => new { si.Id, si.CompanyId, si.ShiftTypeId, si.WorkDate })
            .FirstOrDefaultAsync(ct);

        if (instance is null)
            return new JsonResult(new { error = "no_instance" });

        var instanceMoleculeId = await _db.Companies
            .IgnoreQueryFilters()
            .Where(c => c.Id == instance.CompanyId)
            .Select(c => c.MoleculeId)
            .FirstOrDefaultAsync(ct);

        if (instanceMoleculeId != MoleculeId.Value)
            return Forbid();

        DateOnly periodStart, periodEnd;
        ResolveJusticePeriod(out periodStart, out periodEnd);

        var parentScope = new JusticeQuery(
            Scope: JusticeScope.Company,
            ScopeId: instance.CompanyId,
            PeriodStart: periodStart,
            PeriodEnd: periodEnd,
            WorkType: JusticeWorkType.Shift,
            ExcludeExemptShifts: true,
            Level: JusticeLevel.UsersInCompany);

        var hole = new HoleSelector(
            Kind: "shift",
            ShiftInstanceId: instance.Id,
            Date: instance.WorkDate,
            CompanyId: instance.CompanyId,
            JobTypeId: null,
            DutyTypeValue: null,
            MoleculeId: null,
            ParentScope: parentScope);

        var preview = await _justiceService.PreviewImpactAsync(new SimulatedAssignment(userId, hole), ct);
        return new JsonResult(BuildPreviewJson(preview));
    }

    private object BuildEligibilityJson(EligibleCandidatesViewModel result, int shiftInstanceId, int shiftTypeId, DateOnly date)
    {
        return new
        {
            shiftInstanceId,
            shiftTypeId,
            date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            kind = "shift",
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
                warnings = c.Eligibility.WarningKeys.Select(k => _localizer[k].Value).ToList()
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
