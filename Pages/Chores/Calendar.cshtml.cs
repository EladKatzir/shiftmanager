using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using ShiftManager.Models;
using ShiftManager.Pages;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;
using System.Text.Json;

namespace ShiftManager.Pages.Chores;

[Authorize(Policy = "Grant:ManagerHomeAccess")]
public class CalendarModel : LocalizedPageModel
{
    private readonly IChoreService _choreService;
    private readonly INotificationService _notificationService;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<CalendarModel> _logger;

    public CalendarModel(
        IChoreService choreService,
        INotificationService notificationService,
        IAuditLogService auditLogService,
        ILogger<CalendarModel> logger,
        IStringLocalizer<SharedResources> localizer) : base(localizer)
    {
        _choreService = choreService;
        _notificationService = notificationService;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    // Display properties
    public int Year { get; set; }
    public int Month { get; set; }
    public List<Chore> Chores { get; set; } = new();
    public List<AppUser> EligibleAssignees { get; set; } = new();
    public Dictionary<DateOnly, List<Chore>> ChoresByDate { get; set; } = new();

    // ✅ PHASE 7: Calendar header contextual metrics
    public int TotalChoresCount { get; set; }
    public int UnassignedChoresCount { get; set; }
    public int MyChoresCount { get; set; }

    // Form properties
    [BindProperty]
    public int AssigneeId { get; set; }

    [BindProperty]
    public DateOnly ChoreDate { get; set; }

    [BindProperty]
    public string ChoreTitle { get; set; } = string.Empty;

    [BindProperty]
    public string? ChoreNotes { get; set; }

    [BindProperty]
    public int ChoreId { get; set; }

    [BindProperty]
    public int ShiftAssignmentId { get; set; }

    [BindProperty]
    public string? ConflictAction { get; set; }

    // Message / ErrorMessage removed — feedback now flows through TempData → _Layout FeedbackModal bridge.

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

        // Get chores for the month
        var startDate = new DateOnly(Year, Month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        Chores = await _choreService.GetChoresAsync(
            startDate: startDate,
            endDate: endDate,
            includeCancel: false);

        // Group chores by date for calendar display
        ChoresByDate = Chores
            .GroupBy(c => c.Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Get eligible assignees for the create modal
        EligibleAssignees = await _choreService.GetEligibleAssigneesAsync();

        // ✅ PHASE 7: Calculate header metrics
        TotalChoresCount = Chores.Count;
        UnassignedChoresCount = Chores.Count(c => c.UserId == 0); // UserId is required int, 0 means unset

        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(userIdClaim, out var currentUserId))
        {
            MyChoresCount = Chores.Count(c => c.UserId == currentUserId);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostCreateChoreAsync()
    {
        try
        {
            // ✅ SECURITY FIX: Input validation
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = _localizer["Chore_Error_FillRequiredFields"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                return await OnGetAsync();
            }

            if (string.IsNullOrWhiteSpace(ChoreTitle))
            {
                TempData["ErrorMessage"] = _localizer["Chore_Error_TitleRequired"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                return await OnGetAsync();
            }

            // Validate title length (prevent DoS and database errors)
            if (ChoreTitle.Length > 200)
            {
                TempData["ErrorMessage"] = _localizer["Chore_Error_TitleTooLong"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                return await OnGetAsync();
            }

            // Validate notes length
            if (!string.IsNullOrWhiteSpace(ChoreNotes) && ChoreNotes.Length > 1000)
            {
                TempData["ErrorMessage"] = _localizer["Chore_Error_NotesTooLong"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                return await OnGetAsync();
            }

            // Validate assignee ID
            if (AssigneeId <= 0)
            {
                TempData["ErrorMessage"] = _localizer["Chore_Error_InvalidAssignee"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                return await OnGetAsync();
            }

            // Validate date (prevent far future dates)
            if (ChoreDate > DateOnly.FromDateTime(DateTime.Today.AddYears(2)))
            {
                TempData["ErrorMessage"] = _localizer["Chore_Error_DateTooFarFuture"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                return await OnGetAsync();
            }

            // Validate date (prevent past dates)
            if (ChoreDate < DateOnly.FromDateTime(DateTime.Today))
            {
                TempData["ErrorMessage"] = _localizer["Chore_Error_DateInPast"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                return await OnGetAsync();
            }

            // Check if user has permission
            var currentUserId = GetCurrentUserId();
            if (!await _choreService.CanUserManageChoresAsync(currentUserId))
            {
                TempData["ErrorMessage"] = _localizer["Chore_Error_NoPermissionCreate"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                return await OnGetAsync();
            }

            // Check if assignee is valid
            if (!await _choreService.CanUserManageChoreForAssigneeAsync(currentUserId, AssigneeId))
            {
                TempData["ErrorMessage"] = _localizer["Chore_Error_CannotAssignToUser"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                return await OnGetAsync();
            }

            // Attempt to create the chore
            var result = await _choreService.CreateChoreAsync(
                assigneeId: AssigneeId,
                date: ChoreDate,
                title: ChoreTitle,
                notes: ChoreNotes);

            if (!result.Success)
            {
                // Check if it's a shift conflict
                if (result.Message == "SHIFT_CONFLICT")
                {
                    // Get the shift assignment for conflict resolution UI
                    var shift = await _choreService.GetShiftOnDateAsync(AssigneeId, ChoreDate);
                    if (shift != null)
                    {
                        TempData["ShowConflictDialog"] = true;
                        TempData["ConflictShiftId"] = shift.Id;
                        TempData["ConflictAssigneeId"] = AssigneeId;
                        TempData["ConflictDate"] = ChoreDate.ToString("yyyy-MM-dd");
                        TempData["ConflictTitle"] = ChoreTitle;
                        TempData["ConflictNotes"] = ChoreNotes;
                        TempData["ErrorMessage"] = _localizer["Chore_Error_ShiftConflictReplace"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                    }
                    else
                    {
                        TempData["ErrorMessage"] = _localizer["Chore_Error_ShiftConflict"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                    }
                }
                else
                {
                    TempData["ErrorMessage"] = result.Message; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                }
                return await OnGetAsync(ChoreDate.Year, ChoreDate.Month);
            }

            // Success - send notification and audit log
            await _notificationService.CreateChoreAssignedNotificationAsync(
                AssigneeId,
                ChoreTitle,
                ChoreDate,
                result.Chore!.Id);

            await _auditLogService.LogAsync(
                action: "ChoreCreated",
                entityType: "Chore",
                entityId: result.Chore.Id,
                description: $"Created chore '{ChoreTitle}' for user {AssigneeId} on {ChoreDate:yyyy-MM-dd}",
                details: JsonSerializer.Serialize(new { ChoreId = result.Chore.Id, AssigneeId, ChoreDate, ChoreTitle, ChoreNotes }));

            TempData["SuccessMessage"] = _localizer["Chore_Success_Created", ChoreTitle].Value;
            return RedirectToPage(new { year = ChoreDate.Year, month = ChoreDate.Month });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating chore");
            TempData["ErrorMessage"] = _localizer["Chore_Error_CreatingFailed"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            return await OnGetAsync();
        }
    }

    public async Task<IActionResult> OnPostReplaceShiftWithChoreAsync()
    {
        try
        {
            // ✅ SECURITY FIX: Input validation
            if (ShiftAssignmentId <= 0 || string.IsNullOrWhiteSpace(ChoreTitle))
            {
                TempData["ErrorMessage"] = _localizer["Chore_Error_InvalidRequest"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                return await OnGetAsync();
            }

            // Validate title length
            if (ChoreTitle.Length > 200)
            {
                TempData["ErrorMessage"] = _localizer["Chore_Error_TitleTooLong"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                return await OnGetAsync();
            }

            // Validate notes length
            if (!string.IsNullOrWhiteSpace(ChoreNotes) && ChoreNotes.Length > 1000)
            {
                TempData["ErrorMessage"] = _localizer["Chore_Error_NotesTooLong"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                return await OnGetAsync();
            }

            // Check permissions
            var currentUserId = GetCurrentUserId();
            if (!await _choreService.CanUserManageChoresAsync(currentUserId))
            {
                TempData["ErrorMessage"] = _localizer["Chore_Error_NoPermissionReplaceShifts"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                return await OnGetAsync();
            }

            var result = await _choreService.ReplaceShiftWithChoreAsync(
                shiftAssignmentId: ShiftAssignmentId,
                title: ChoreTitle,
                notes: ChoreNotes);

            if (!result.Success)
            {
                TempData["ErrorMessage"] = result.Message; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                return await OnGetAsync();
            }

            // Get assignee for notification
            var chore = result.Chore!;

            // Send notifications
            await _notificationService.CreateChoreAssignedNotificationAsync(
                chore.UserId,
                ChoreTitle,
                chore.Date,
                chore.Id);

            await _auditLogService.LogAsync(
                action: "ShiftReplacedWithChore",
                entityType: "Chore",
                entityId: chore.Id,
                description: $"Replaced shift {ShiftAssignmentId} with chore '{ChoreTitle}' for user {chore.UserId} on {chore.Date:yyyy-MM-dd}",
                details: JsonSerializer.Serialize(new { ChoreId = chore.Id, ShiftAssignmentId, ChoreTitle, ChoreNotes }));

            TempData["SuccessMessage"] = _localizer["Chore_Success_ShiftReplaced", ChoreTitle].Value;
            return RedirectToPage(new { year = chore.Date.Year, month = chore.Date.Month });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error replacing shift with chore");
            TempData["ErrorMessage"] = _localizer["Chore_Error_ReplacingShiftFailed"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            return await OnGetAsync();
        }
    }

    public async Task<IActionResult> OnPostCancelChoreAsync()
    {
        try
        {
            // ✅ SECURITY FIX: Validate input and permissions
            // Manually read ChoreId from form to avoid binding conflicts
            var choreIdString = Request.Form["ChoreId"].ToString();

            if (!int.TryParse(choreIdString, out var choreId) || choreId <= 0)
            {
                TempData["ErrorMessage"] = _localizer["Chore_Error_InvalidId"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                return await OnGetAsync();
            }

            // Use the manually parsed choreId instead of the property
            ChoreId = choreId;

            // Check permissions
            var currentUserId = GetCurrentUserId();
            if (!await _choreService.CanUserManageChoresAsync(currentUserId))
            {
                _logger.LogWarning("SECURITY: User {UserId} attempted to cancel chore {ChoreId} without permission", currentUserId, ChoreId);
                TempData["ErrorMessage"] = _localizer["Chore_Error_NoPermissionCancel"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                return await OnGetAsync();
            }

            // Get the chore before canceling for notification purposes
            var chore = await _choreService.GetChoreByIdAsync(ChoreId);
            if (chore == null)
            {
                TempData["ErrorMessage"] = _localizer["Chore_Error_NotFound"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                return await OnGetAsync();
            }

            var result = await _choreService.CancelChoreAsync(ChoreId);

            if (!result.Success)
            {
                TempData["ErrorMessage"] = result.Message; TempData["ErrorId"] = HttpContext.TraceIdentifier;
                return await OnGetAsync();
            }

            // Send notification
            await _notificationService.CreateChoreCanceledNotificationAsync(
                chore.UserId,
                chore.Title,
                chore.Date,
                ChoreId);

            await _auditLogService.LogAsync(
                action: "ChoreCanceled",
                entityType: "Chore",
                entityId: ChoreId,
                description: $"Canceled chore '{chore.Title}' for user {chore.UserId} on {chore.Date:yyyy-MM-dd}",
                details: JsonSerializer.Serialize(new { ChoreId, ChoreTitle = chore.Title, UserId = chore.UserId, ChoreDate = chore.Date }));

            TempData["SuccessMessage"] = _localizer["Chore_Success_Canceled", chore.Title].Value;
            return RedirectToPage(new { year = chore.Date.Year, month = chore.Date.Month });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error canceling chore");
            TempData["ErrorMessage"] = _localizer["Chore_Error_CancelingFailed"].Value; TempData["ErrorId"] = HttpContext.TraceIdentifier;
            return await OnGetAsync();
        }
    }
}
