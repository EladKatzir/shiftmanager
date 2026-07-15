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
/// "Team" = the users of a chosen (Company × JobType), rendered IDENTICALLY to /Calendar/Overview
/// (same <see cref="IOverviewCalendarBuilder"/>, same ExcelCalendarTable component) — design doc
/// docs/superpowers/specs/2026-07-14-ui-batch-and-team-page-design.md #9.3/#9.4.
///
/// Visible to EVERY authenticated user ([Authorize], no policy — required for the Task #10 nav
/// leaf's null-policy parity check); the picker itself is scope-limited to the companies the
/// caller holds "ViewShifts" for (+ their own company). Real use case: an Alhut lead in Tzafona
/// sees all Alhut soldiers in Tzafona by default, and can switch Company to see Alhut in another
/// company within their ViewShifts scope (JobType is area-scoped — the same JobType id spans
/// companies in the area, which is what makes the cross-company switch meaningful).
///
/// v1 is READ-ONLY: BuildAsync is always called with canEditNotes:false. Overview's per-cell note
/// editing is deliberately not wired here for v1 (see task-9-report.md).
///
/// Saved views: private per-owner <see cref="DeskTeamView"/> rows (a (TargetCompany, JobType, Name)
/// tuple) — deliberately a separate entity/service from TeamCalendar so /MyTeam stays untouched.
/// </summary>
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE. `accessibleCompanyIds` is
// derived from the caller's own "ViewShifts" grants (GetAccessibleCompanyIdsForGrantAsync) union
// their own company — never from client input. SelectedCompanyId, wherever it is used to load
// data, has ALWAYS been validated against that set first: either it was left at its (always-safe)
// default, resolved from an already-scope-checked saved DeskTeamView, or explicitly re-checked
// against accessibleCompanyIds (Forbid on failure) a few lines above. IgnoreQueryFilters is
// required throughout because the company being viewed is frequently NOT the caller's own tenant
// (that's the whole point of the cross-company Team switch) — the explicit `== SelectedCompanyId`
// (itself pre-validated) filter is what keeps every query correctly scoped.
[Authorize]
public class TeamModel : PageModel
{
    private const string ViewShiftsGrantKey = "ViewShifts";

    private readonly AppDbContext _db;
    private readonly IGrantService _grantService;
    private readonly IJobTypeService _jobTypeService;
    private readonly ITenantResolver _tenantResolver;
    private readonly IOverviewCalendarBuilder _calendarBuilder;
    private readonly IDeskTeamViewService _deskTeamViewService;
    private readonly IStringLocalizer<SharedResources> _localizer;
    private readonly ILogger<TeamModel> _logger;

    public TeamModel(
        AppDbContext db,
        IGrantService grantService,
        IJobTypeService jobTypeService,
        ITenantResolver tenantResolver,
        IOverviewCalendarBuilder calendarBuilder,
        IDeskTeamViewService deskTeamViewService,
        IStringLocalizer<SharedResources> localizer,
        ILogger<TeamModel> logger)
    {
        _db = db;
        _grantService = grantService;
        _jobTypeService = jobTypeService;
        _tenantResolver = tenantResolver;
        _calendarBuilder = calendarBuilder;
        _deskTeamViewService = deskTeamViewService;
        _localizer = localizer;
        _logger = logger;
    }

    // ---- Query / picker parameters (all client-supplied — treated as untrusted) ----

    [BindProperty(SupportsGet = true)]
    public int? SelectedCompanyId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? SelectedJobTypeId { get; set; }

    /// <summary>When set, opens a saved DeskTeamView (re-validated against current scope — #9.4).</summary>
    [BindProperty(SupportsGet = true)]
    public int? ViewId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Start { get; set; }

    [BindProperty(SupportsGet = true)]
    public string ViewMode { get; set; } = "week"; // week, next7, 2weeks, month

    // ---- Page state ----

    public int CurrentUserId { get; set; }
    public List<Company> AccessibleCompanies { get; set; } = new();
    public List<JobType> AvailableJobTypes { get; set; } = new();
    public List<AppUser> Users { get; set; } = new();
    public ExcelCalendarTableViewModel CalendarData { get; set; } = new();
    public List<DeskTeamView> SavedViews { get; set; } = new();

    /// <summary>Id of the saved view currently being displayed (highlights its tab), if any.</summary>
    public int? ActiveViewId { get; set; }

    /// <summary>
    /// True when a ?ViewId= was supplied but either doesn't exist / isn't owned by the caller, or
    /// its TargetCompanyId has fallen outside the caller's CURRENT ViewShifts scope (grant revoked
    /// since the view was saved). The view is never rendered in this case — the page instead falls
    /// back to the caller's own company/job type and surfaces this flag for the UI.
    /// </summary>
    public bool RequestedViewInaccessible { get; set; }

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string PreviousStart { get; set; } = string.Empty;
    public string NextStart { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
        {
            _logger.LogError("Invalid or missing NameIdentifier claim");
            return RedirectToPage("/Error");
        }
        CurrentUserId = currentUserId;

        var ownCompanyId = _tenantResolver.GetCurrentTenantId();
        if (ownCompanyId <= 0)
        {
            _logger.LogWarning("User {UserId} has no company context", currentUserId);
            return RedirectToPage("/Error");
        }

        // ---- Scope resolution: the picker's own boundary. Never derived from client input. ----
        var accessibleCompanyIds = new HashSet<int>(
            await _grantService.GetAccessibleCompanyIdsForGrantAsync(currentUserId, ViewShiftsGrantKey));
        accessibleCompanyIds.Add(ownCompanyId); // caller can always see their own company

        // Capture what the CALLER directly asked for, before ViewId resolution (below) may
        // overwrite SelectedCompanyId with an already-validated value — this is what the
        // anti-IDOR gate re-checks a few lines down.
        var directlyRequestedCompanyId = SelectedCompanyId;

        // ---- Saved-view open request (render-time IDOR re-check — spec #9.4) ----
        if (ViewId.HasValue)
        {
            var ownedViews = await _deskTeamViewService.ListForOwnerAsync();
            var requestedView = ownedViews.FirstOrDefault(v => v.Id == ViewId.Value);

            if (requestedView == null || !accessibleCompanyIds.Contains(requestedView.TargetCompanyId))
            {
                // Not found, not owned by the caller (ListForOwnerAsync is already owner-scoped —
                // this can't leak another owner's view), or the grant that made it visible has
                // since been revoked. Either way: do NOT render it. Graceful, not a hard Forbid —
                // this is a normal lifecycle event (scope changed after the view was saved), not
                // an attack; the caller falls back to their own company below.
                RequestedViewInaccessible = true;
            }
            else
            {
                SelectedCompanyId = requestedView.TargetCompanyId;
                SelectedJobTypeId = requestedView.JobTypeId;
                ActiveViewId = requestedView.Id;
            }
        }

        // ---- Anti-IDOR gate: a directly-crafted SelectedCompanyId outside scope is hard-rejected.
        // (Only applies when the effective selection did NOT come from an already-validated saved
        // view above — ActiveViewId is null in that case.) The picker never offers an out-of-scope
        // company, so reaching this branch means the query string was tampered with. ----
        if (!ActiveViewId.HasValue && directlyRequestedCompanyId.HasValue
            && !accessibleCompanyIds.Contains(directlyRequestedCompanyId.Value))
        {
            _logger.LogWarning(
                "User {UserId} requested Team company {CompanyId} outside their ViewShifts scope",
                currentUserId, directlyRequestedCompanyId.Value);
            return Forbid();
        }

        // ---- Defaults for anything still unset ----
        var callerUser = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == currentUserId);
        if (!SelectedCompanyId.HasValue)
            SelectedCompanyId = ownCompanyId;
        if (!SelectedJobTypeId.HasValue)
            SelectedJobTypeId = callerUser?.JobTypeId;

        // SECURITY-AUDITED: SAFE — scoped to accessibleCompanyIds (grant-derived + own company).
        AccessibleCompanies = await _db.Companies
            .IgnoreQueryFilters()
            .Where(c => accessibleCompanyIds.Contains(c.Id))
            .OrderBy(c => c.Name)
            .ToListAsync();

        // ---- JobType picker for the resolved company ----
        // SelectedCompanyId is provably a member of accessibleCompanyIds by this point (every path
        // above that sets it validates membership first), and AccessibleCompanies is exactly
        // {existing Company rows} ∩ accessibleCompanyIds — so a lookup miss here can only mean the
        // company row itself doesn't exist, in which case a second query would find nothing either.
        var selectedCompany = AccessibleCompanies.FirstOrDefault(c => c.Id == SelectedCompanyId.Value);

        if (selectedCompany?.MoleculeId != null)
        {
            AvailableJobTypes = await _jobTypeService.GetJobTypesForMoleculeAsync(selectedCompany.MoleculeId.Value);
        }

        if (!SelectedJobTypeId.HasValue || !AvailableJobTypes.Any(jt => jt.Id == SelectedJobTypeId.Value))
        {
            SelectedJobTypeId = AvailableJobTypes.FirstOrDefault()?.Id;
        }

        CalculateDateRange();

        if (SelectedJobTypeId.HasValue)
        {
            await LoadUsersAsync();

            // Shared builder (Task #9.2) — same pipeline Overview uses, so the calendar renders
            // identically by construction. v1 is read-only (no per-cell note editing yet).
            CalendarData = await _calendarBuilder.BuildAsync(
                SelectedCompanyId.Value, Users, StartDate, EndDate, ViewMode, canEditNotes: false);

            // Task #7 left RowOrderContextKey as "overview:{companyId}" — Team ordering must be
            // independent from Overview's, or dragging rows on one page would silently reorder
            // the other.
            CalendarData.RowOrderContextKey = $"team:{SelectedCompanyId}:{SelectedJobTypeId}";
        }

        SavedViews = await _deskTeamViewService.ListForOwnerAsync();

        _logger.LogInformation(
            "Team calendar loaded for User {UserId}, Company {CompanyId}, JobType {JobTypeId}",
            currentUserId, SelectedCompanyId, SelectedJobTypeId);

        return Page();
    }

    /// <summary>
    /// Loads the row set: Standard-account users of the resolved (Company × JobType). Internal
    /// (not private) so tests can call it directly, mirroring OverviewModel.LoadUsersAsync.
    /// </summary>
    internal async Task LoadUsersAsync()
    {
        // SECURITY-AUDITED: SAFE — SelectedCompanyId has ALREADY been validated (in OnGetAsync,
        // above) to be a member of accessibleCompanyIds (ViewShifts-grant-derived, or the caller's
        // own company, or an already-scope-checked saved view). IgnoreQueryFilters is required
        // because the company being viewed is frequently not the caller's own tenant (the explicit
        // CompanyId == filter — against the pre-validated SelectedCompanyId — is what re-scopes it
        // correctly; no unvalidated value ever reaches this query).
        Users = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.CompanyId == SelectedCompanyId!.Value
                     && u.JobTypeId == SelectedJobTypeId!.Value
                     && u.AccountType == AccountType.Standard
                     && u.IsActive)
            .OrderBy(u => u.DisplayName)
            .ToListAsync();
    }

    private void CalculateDateRange()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        // "Next 7 days" is anchored to TODAY (today..today+6), ignoring Start; nav is inert —
        // mirrors Overview's identical ViewMode semantics.
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
            StartDate = GetStartOfWeek(today);
        }

        EndDate = ViewMode switch
        {
            "2weeks" => StartDate.AddDays(13),
            "month" => StartDate.AddDays(DateTime.DaysInMonth(StartDate.Year, StartDate.Month) - 1),
            _ => StartDate.AddDays(6)
        };

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

    // ---- Saved-view handlers (DeskTeamView — private, owner-scoped; separate from TeamCalendar) ----

    /// <summary>Creates a new saved view from the current picker selection.</summary>
    public async Task<IActionResult> OnPostAddViewAsync(string? name)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
            return new JsonResult(new { success = false, error = "Unauthorized" }) { StatusCode = 401 };

        if (!SelectedCompanyId.HasValue || !SelectedJobTypeId.HasValue)
            return new JsonResult(new { success = false, error = _localizer["Team_MissingSelection"].Value }) { StatusCode = 400 };

        var trimmedName = (name ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmedName))
            return new JsonResult(new { success = false, error = _localizer["Team_NameRequired"].Value }) { StatusCode = 400 };
        if (trimmedName.Length > 60)
            return new JsonResult(new { success = false, error = _localizer["Team_NameTooLong"].Value }) { StatusCode = 400 };

        // SECURITY: re-check scope for THIS write — SelectedCompanyId is bound from the request
        // and a POST handler is a separate authorization context from the GET that rendered the form.
        var ownCompanyId = _tenantResolver.GetCurrentTenantId();
        var accessibleCompanyIds = new HashSet<int>(
            await _grantService.GetAccessibleCompanyIdsForGrantAsync(currentUserId, ViewShiftsGrantKey));
        accessibleCompanyIds.Add(ownCompanyId);
        if (!accessibleCompanyIds.Contains(SelectedCompanyId.Value))
        {
            _logger.LogWarning(
                "User {UserId} attempted to save a Team view for out-of-scope company {CompanyId}",
                currentUserId, SelectedCompanyId.Value);
            return new JsonResult(new { success = false, error = _localizer["Calendar_Error_InsufficientPermissions"].Value }) { StatusCode = 403 };
        }

        // Pre-check the DB's unique (CompanyId,OwnerId,Name) constraint so a duplicate name is
        // always a clean validation error, never a raw DbUpdateException/500.
        var existingViews = await _deskTeamViewService.ListForOwnerAsync();
        if (existingViews.Any(v => string.Equals(v.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
            return new JsonResult(new { success = false, error = _localizer["Team_DuplicateName"].Value }) { StatusCode = 400 };

        try
        {
            var created = await _deskTeamViewService.CreateAsync(SelectedCompanyId.Value, SelectedJobTypeId.Value, trimmedName);
            return new JsonResult(new { success = true, id = created.Id, name = created.Name });
        }
        catch (DbUpdateException ex)
        {
            // Defense-in-depth against the TOCTOU race on the DB's unique index — a second
            // request could win between the pre-check above and this insert.
            _logger.LogWarning(ex, "DeskTeamView create raced a duplicate name for user {UserId}", currentUserId);
            return new JsonResult(new { success = false, error = _localizer["Team_DuplicateName"].Value }) { StatusCode = 400 };
        }
    }

    /// <summary>Deletes (soft) a saved view. The service itself is owner-scoped (throws if not found/owned).</summary>
    public async Task<IActionResult> OnPostDeleteViewAsync(int id)
    {
        try
        {
            await _deskTeamViewService.DeleteAsync(id);
            return new JsonResult(new { success = true });
        }
        catch (InvalidOperationException)
        {
            return new JsonResult(new { success = false, error = _localizer["Team_ViewNotFound"].Value }) { StatusCode = 404 };
        }
    }
}
