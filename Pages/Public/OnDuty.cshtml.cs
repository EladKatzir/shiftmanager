using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Pages;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;
using System.Text.Json;

namespace ShiftManager.Pages.Public;

[Authorize(Policy = "CanViewOnDuty")]
public class OnDutyModel : LocalizedPageModel
{
    private readonly IOnDutyService _onDutyService;
    private readonly INotificationService _notificationService;
    private readonly IAuditLogService _auditLogService;
    private readonly IBusyUserService _busyUserService;
    private readonly ILogger<OnDutyModel> _logger;
    // ✅ PHASE 18: Add database context to load custom OnDutyTypeConfigs
    private readonly AppDbContext _db;

    public OnDutyModel(
        IOnDutyService onDutyService,
        INotificationService notificationService,
        IAuditLogService auditLogService,
        IBusyUserService busyUserService,
        ILogger<OnDutyModel> logger,
        AppDbContext db,
        IStringLocalizer<SharedResources> localizer) : base(localizer)
    {
        _onDutyService = onDutyService;
        _notificationService = notificationService;
        _auditLogService = auditLogService;
        _busyUserService = busyUserService;
        _logger = logger;
        _db = db;
    }

    // Display properties
    public int Year { get; set; }
    public int Month { get; set; }
    public List<Models.OnDuty> OnDuties { get; set; } = new();
    public List<AppUser> EligibleAssignees { get; set; } = new();
    public Dictionary<DateOnly, List<Models.OnDuty>> OnDutiesByDate { get; set; } = new();

    // ✅ PHASE 18: Add OnDutyTypeConfigs collection for dropdown population
    public List<OnDutyTypeConfig> CustomOnDutyTypes { get; set; } = new();

    // Busy user status per date: [Date][UserId] => BusyStatus
    public Dictionary<DateOnly, Dictionary<int, BusyStatus>> BusyUsersByDate { get; set; } = new();

    // ✅ PHASE 7: Calendar header contextual metrics
    public int TotalOnDutyCount { get; set; }
    public int HakamCount { get; set; }
    public int LeadCount { get; set; }
    public int MyOnDutyCount { get; set; }

    // Form properties
    [BindProperty]
    public int AssigneeId { get; set; }

    [BindProperty]
    public DateOnly OnDutyDate { get; set; }

    [BindProperty]
    public OnDutyType OnDutyType { get; set; }

    [BindProperty]
    public string? OnDutyNotes { get; set; }

    [BindProperty]
    public int OnDutyId { get; set; }

    public string? Message { get; set; }
    public string? ErrorMessage { get; set; }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : 0;
    }

    public async Task<IActionResult> OnGetAsync(int? year = null, int? month = null)
    {
        var today = DateTime.UtcNow;
        Year = year ?? today.Year;
        Month = month ?? today.Month;

        // Validate month/year
        if (Month < 1 || Month > 12)
        {
            Month = today.Month;
        }

        // Get on-duty assignments for the month
        var startDate = new DateOnly(Year, Month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        OnDuties = await _onDutyService.GetOnDutiesAsync(
            startDate: startDate,
            endDate: endDate,
            includeCanceled: false);

        // Group on-duty assignments by date for calendar display
        OnDutiesByDate = OnDuties
            .GroupBy(o => o.Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Get eligible assignees for the create modal
        EligibleAssignees = await _onDutyService.GetEligibleAssigneesAsync();

        // ✅ PHASE 18: Load custom OnDuty types from database (TypeValue >= 2)
        CustomOnDutyTypes = await _db.OnDutyTypeConfigs
            .Where(t => t.IsActive)
            .OrderBy(t => t.TypeValue)
            .ToListAsync();

        // ✅ PHASE 7: Calculate header metrics
        TotalOnDutyCount = OnDuties.Count;
        HakamCount = OnDuties.Count(o => o.Type == OnDutyType.Hakam);
        LeadCount = OnDuties.Count(o => o.Type == OnDutyType.Lead);

        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(userIdClaim, out var currentUserId))
        {
            MyOnDutyCount = OnDuties.Count(o => o.UserId == currentUserId);
        }

        // Load busy user status for all dates in the month (for dropdown color coding)
        var daysInMonth = DateTime.DaysInMonth(Year, Month);
        for (int day = 1; day <= daysInMonth; day++)
        {
            var date = new DateOnly(Year, Month, day);
            var busyForDate = await _busyUserService.GetBusyUsersAsync(date, TimeOnly.MinValue, TimeOnly.MaxValue);
            BusyUsersByDate[date] = busyForDate;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostCreateOnDutyAsync()
    {
        try
        {
            // Input validation
            if (!ModelState.IsValid)
            {
                ErrorMessage = _localizer["OnDuty_Error_FillRequiredFields"];
                return await OnGetAsync();
            }

            // Validate assignee ID
            if (AssigneeId <= 0)
            {
                ErrorMessage = _localizer["OnDuty_Error_InvalidAssignee"];
                return await OnGetAsync();
            }

            // Validate notes length
            if (!string.IsNullOrWhiteSpace(OnDutyNotes) && OnDutyNotes.Length > 1000)
            {
                ErrorMessage = _localizer["OnDuty_Error_NotesTooLong"];
                return await OnGetAsync();
            }

            // Validate date (prevent far future dates)
            if (OnDutyDate > DateOnly.FromDateTime(DateTime.Today.AddYears(2)))
            {
                ErrorMessage = _localizer["OnDuty_Error_DateTooFarFuture"];
                return await OnGetAsync();
            }

            // Validate date (prevent past dates)
            if (OnDutyDate < DateOnly.FromDateTime(DateTime.Today))
            {
                ErrorMessage = _localizer["OnDuty_Error_DateInPast"];
                return await OnGetAsync();
            }

            // Check if user has permission
            var currentUserId = GetCurrentUserId();
            if (!await _onDutyService.CanUserManageOnDutyAsync(currentUserId))
            {
                ErrorMessage = _localizer["OnDuty_Error_NoPermissionCreate"];
                return await OnGetAsync();
            }

            // Attempt to create the on-duty assignment
            var result = await _onDutyService.CreateOnDutyAsync(
                assigneeId: AssigneeId,
                date: OnDutyDate,
                type: OnDutyType,
                notes: OnDutyNotes);

            if (!result.Success)
            {
                // Check if it's a vacation conflict
                if (result.Message == "VACATION_CONFLICT")
                {
                    ErrorMessage = _localizer["OnDuty_Error_VacationConflict"];
                }
                else
                {
                    ErrorMessage = result.Message;
                }
                return await OnGetAsync(OnDutyDate.Year, OnDutyDate.Month);
            }

            // Success - notify user and audit log
            await _notificationService.CreateOnDutyAssignedNotificationAsync(
                userId: AssigneeId,
                onDutyType: OnDutyType,
                onDutyDate: OnDutyDate,
                onDutyId: result.OnDuty!.Id);

            await _auditLogService.LogAsync(
                action: "OnDutyCreated",
                entityType: "OnDuty",
                entityId: result.OnDuty.Id,
                description: $"Created on-duty {OnDutyType} for user {AssigneeId} on {OnDutyDate:yyyy-MM-dd}",
                details: JsonSerializer.Serialize(new { OnDutyId = result.OnDuty.Id, AssigneeId, OnDutyDate, OnDutyType, OnDutyNotes }));

            Message = _localizer["OnDuty_Success_Created"];
            return RedirectToPage(new { year = OnDutyDate.Year, month = OnDutyDate.Month, message = Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating on-duty assignment");
            ErrorMessage = _localizer["OnDuty_Error_CreatingFailed"];
            return await OnGetAsync();
        }
    }

    public async Task<IActionResult> OnPostCancelOnDutyAsync()
    {
        try
        {
            // Validate input and permissions
            var onDutyIdString = Request.Form["OnDutyId"].ToString();

            if (!int.TryParse(onDutyIdString, out var onDutyId) || onDutyId <= 0)
            {
                ErrorMessage = _localizer["OnDuty_Error_InvalidId"];
                return await OnGetAsync();
            }

            OnDutyId = onDutyId;

            // Check permissions
            var currentUserId = GetCurrentUserId();
            if (!await _onDutyService.CanUserManageOnDutyAsync(currentUserId))
            {
                _logger.LogWarning("SECURITY: User {UserId} attempted to cancel on-duty {OnDutyId} without permission", currentUserId, OnDutyId);
                ErrorMessage = _localizer["OnDuty_Error_NoPermissionCancel"];
                return await OnGetAsync();
            }

            // Get the on-duty before canceling
            var onDuty = await _onDutyService.GetOnDutyByIdAsync(OnDutyId);
            if (onDuty == null)
            {
                ErrorMessage = _localizer["OnDuty_Error_NotFound"];
                return await OnGetAsync();
            }

            var result = await _onDutyService.CancelOnDutyAsync(OnDutyId);

            if (!result.Success)
            {
                ErrorMessage = result.Message;
                return await OnGetAsync();
            }

            // Notify user and audit log
            await _notificationService.CreateOnDutyCanceledNotificationAsync(
                userId: onDuty.UserId,
                onDutyType: onDuty.Type,
                onDutyDate: onDuty.Date,
                onDutyId: OnDutyId);

            await _auditLogService.LogAsync(
                action: "OnDutyCanceled",
                entityType: "OnDuty",
                entityId: OnDutyId,
                description: $"Canceled on-duty {onDuty.Type} for user {onDuty.UserId} on {onDuty.Date:yyyy-MM-dd}",
                details: JsonSerializer.Serialize(new { OnDutyId, OnDutyType = onDuty.Type, UserId = onDuty.UserId, OnDutyDate = onDuty.Date }));

            Message = _localizer["OnDuty_Success_Canceled"];
            return RedirectToPage(new { year = onDuty.Date.Year, month = onDuty.Date.Month, message = Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error canceling on-duty assignment");
            ErrorMessage = _localizer["OnDuty_Error_CancelingFailed"];
            return await OnGetAsync();
        }
    }
}
