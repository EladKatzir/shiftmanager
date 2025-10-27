using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShiftManager.Models;
using ShiftManager.Services;
using System.Security.Claims;
using System.Text.Json;

namespace ShiftManager.Pages.Chores;

[Authorize(Policy = "IsManagerOrAdmin")]
public class CalendarModel : PageModel
{
    private readonly IChoreService _choreService;
    private readonly INotificationService _notificationService;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<CalendarModel> _logger;

    public CalendarModel(
        IChoreService choreService,
        INotificationService notificationService,
        IAuditLogService auditLogService,
        ILogger<CalendarModel> logger)
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

        return Page();
    }

    public async Task<IActionResult> OnPostCreateChoreAsync()
    {
        try
        {
            if (!ModelState.IsValid)
            {
                ErrorMessage = "Please fill in all required fields.";
                return await OnGetAsync();
            }

            if (string.IsNullOrWhiteSpace(ChoreTitle))
            {
                ErrorMessage = "Chore title is required.";
                return await OnGetAsync();
            }

            // Check if user has permission
            var currentUserId = GetCurrentUserId();
            if (!await _choreService.CanUserManageChoresAsync(currentUserId))
            {
                ErrorMessage = "You do not have permission to create chores.";
                return await OnGetAsync();
            }

            // Check if assignee is valid
            if (!await _choreService.CanUserManageChoreForAssigneeAsync(currentUserId, AssigneeId))
            {
                ErrorMessage = "You cannot assign chores to this user.";
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
                        ErrorMessage = "This user has a shift on this date. Do you want to replace the shift with this chore?";
                    }
                    else
                    {
                        ErrorMessage = "This user has a shift on this date.";
                    }
                }
                else
                {
                    ErrorMessage = result.Message;
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

            Message = $"Chore '{ChoreTitle}' created successfully.";
            return RedirectToPage(new { year = ChoreDate.Year, month = ChoreDate.Month, message = Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating chore");
            ErrorMessage = "An error occurred while creating the chore.";
            return await OnGetAsync();
        }
    }

    public async Task<IActionResult> OnPostReplaceShiftWithChoreAsync()
    {
        try
        {
            if (ShiftAssignmentId <= 0 || string.IsNullOrWhiteSpace(ChoreTitle))
            {
                ErrorMessage = "Invalid request.";
                return await OnGetAsync();
            }

            var result = await _choreService.ReplaceShiftWithChoreAsync(
                shiftAssignmentId: ShiftAssignmentId,
                title: ChoreTitle,
                notes: ChoreNotes);

            if (!result.Success)
            {
                ErrorMessage = result.Message;
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

            Message = $"Shift replaced with chore '{ChoreTitle}' successfully.";
            return RedirectToPage(new { year = chore.Date.Year, month = chore.Date.Month, message = Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error replacing shift with chore");
            ErrorMessage = "An error occurred while replacing the shift.";
            return await OnGetAsync();
        }
    }

    public async Task<IActionResult> OnPostCancelChoreAsync()
    {
        try
        {
            // Manually read ChoreId from form to avoid binding conflicts
            var choreIdString = Request.Form["ChoreId"].ToString();

            if (!int.TryParse(choreIdString, out var choreId) || choreId <= 0)
            {
                ErrorMessage = "Invalid chore ID.";
                return await OnGetAsync();
            }

            // Use the manually parsed choreId instead of the property
            ChoreId = choreId;

            // Get the chore before canceling for notification purposes
            var chore = await _choreService.GetChoreByIdAsync(ChoreId);
            if (chore == null)
            {
                ErrorMessage = "Chore not found.";
                return await OnGetAsync();
            }

            var result = await _choreService.CancelChoreAsync(ChoreId);

            if (!result.Success)
            {
                ErrorMessage = result.Message;
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

            Message = $"Chore '{chore.Title}' canceled successfully.";
            return RedirectToPage(new { year = chore.Date.Year, month = chore.Date.Month, message = Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error canceling chore");
            ErrorMessage = "An error occurred while canceling the chore.";
            return await OnGetAsync();
        }
    }
}
