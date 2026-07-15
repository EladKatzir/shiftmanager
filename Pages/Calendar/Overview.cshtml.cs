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
    private readonly IOverviewCalendarBuilder _calendarBuilder;
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
        IOverviewCalendarBuilder calendarBuilder,
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
        _calendarBuilder = calendarBuilder;
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

        // Build calendar data (shared builder — Task #9.2 — also used by /Calendar/Team)
        CalendarData = await _calendarBuilder.BuildAsync(CompanyId, Users, StartDate, EndDate, ViewMode, CanEditNotes);

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

    internal async Task LoadUsersAsync()
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
