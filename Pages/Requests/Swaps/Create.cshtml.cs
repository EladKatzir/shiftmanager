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
using System.Security.Claims;

namespace ShiftManager.Pages.Requests.Swaps;

[Authorize]
public class CreateModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly ICompanyContext _companyContext;
    private readonly IGrantService _grantService;
    private readonly IAuditLogService _auditLogService;
    private readonly IBusyService _busyService;
    private readonly INotificationService _notificationService;

    public CreateModel(IStringLocalizer<SharedResources> localizer, AppDbContext db, ICompanyContext companyContext, IGrantService grantService, IAuditLogService auditLogService, IBusyService busyService, INotificationService notificationService)
        : base(localizer)
    {
        _db = db;
        _companyContext = companyContext;
        _grantService = grantService;
        _auditLogService = auditLogService;
        _busyService = busyService;
        _notificationService = notificationService;
    }

    /// <summary>
    /// Busy warnings produced when validating the swap target against the source shift.
    /// Surfaced in the form for the requester and (later) carried into the manager's
    /// approval queue. Empty list = clean swap, no warnings.
    /// </summary>
    public IReadOnlyList<ValidationIssue> SwapTargetWarnings { get; set; } = new List<ValidationIssue>();

    public record AssignmentVM(int AssignmentId, string Label);
    public List<AssignmentVM> MyAssignments { get; set; } = new();
    public List<AppUser> OtherUsers { get; set; } = new();

    [BindProperty] public int? SelectedAssignmentId { get; set; }
    [BindProperty] public int? ToUserId { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            return RedirectToPage("/Auth/Login");
        }

        // Block users without RequestSwap grant (e.g., trainees)
        var hasSwapGrant = await _grantService.HasGrantAsync(userId, "RequestSwap");
        if (!hasSwapGrant)
        {
            return RedirectToPage("/AccessDenied");
        }

        var upcoming = await (from a in _db.ShiftAssignments
                              join si in _db.ShiftInstances on a.ShiftInstanceId equals si.Id
                              join st in _db.ShiftTypes on si.ShiftTypeId equals st.Id
                              where a.UserId == userId && si.WorkDate >= DateOnly.FromDateTime(DateTime.Today)
                              orderby si.WorkDate
                              select new AssignmentVM(a.Id, $"{si.WorkDate:yyyy-MM-dd} {st.Key}")).ToListAsync();
        MyAssignments = upcoming;

        var companyId = _companyContext.GetCompanyIdOrThrow();
        OtherUsers = await _db.Users.Where(u => u.CompanyId == companyId && u.IsActive && u.Id != userId).OrderBy(u => u.DisplayName).ToListAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // SECURITY FIX: Use TryParse to prevent crashes from invalid claims
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            return RedirectToPage("/Auth/Login");
        }

        // Block users without RequestSwap grant (e.g., trainees)
        var hasSwapGrant = await _grantService.HasGrantAsync(userId, "RequestSwap");
        if (!hasSwapGrant)
        {
            return RedirectToPage("/AccessDenied");
        }

        // ✅ SECURITY FIX: Input validation
        if (!SelectedAssignmentId.HasValue || SelectedAssignmentId.Value <= 0)
        {
            ModelState.AddModelError("", _localizer["Error_SelectValidShiftAssignment"].Value);
            await OnGetAsync();
            return Page();
        }

        if (!ToUserId.HasValue || ToUserId.Value <= 0)
        {
            ModelState.AddModelError("", _localizer["Error_SelectValidSwapUser"].Value);
            await OnGetAsync();
            return Page();
        }

        // Prevent swapping with yourself
        if (ToUserId.Value == userId)
        {
            ModelState.AddModelError("", _localizer["Error_CannotSwapWithSelf"].Value);
            await OnGetAsync();
            return Page();
        }

        // Validate that the assignment belongs to the current user (authorization check)
        var assignment = await _db.ShiftAssignments
            .Include(a => a.ShiftInstance)
            .FirstOrDefaultAsync(a => a.Id == SelectedAssignmentId.Value);

        if (assignment == null)
        {
            ModelState.AddModelError("", _localizer["Error_ShiftAssignmentNotFound"].Value);
            await OnGetAsync();
            return Page();
        }

        if (assignment.UserId != userId)
        {
            ModelState.AddModelError("", _localizer["Error_CanOnlySwapOwnShifts"].Value);
            await OnGetAsync();
            return Page();
        }

        // Validate that ToUser is valid and in same company
        var companyId = _companyContext.GetCompanyIdOrThrow();
        var toUser = await _db.Users.FindAsync(ToUserId.Value);

        if (toUser == null || !toUser.IsActive || toUser.CompanyId != companyId)
        {
            ModelState.AddModelError("", _localizer["Error_InvalidSwapTargetUser"].Value);
            await OnGetAsync();
            return Page();
        }

        // Run busy validation against the swap target so the requester sees warnings
        // (e.g., target has a chore on the swap date) before the request is sent. Hard
        // errors block; warnings annotate the request and surface to the approving manager.
        var busyValidation = await _busyService.ValidateAsync(
            new BusyTarget.Shift(assignment.ShiftInstanceId), ToUserId.Value, userId);
        if (!busyValidation.CanProceed)
        {
            foreach (var err in busyValidation.Errors)
                ModelState.AddModelError("", err.Message);
            SwapTargetWarnings = busyValidation.Errors;
            await OnGetAsync();
            return Page();
        }
        SwapTargetWarnings = busyValidation.Warnings;

        var swapRequest = new SwapRequest { FromAssignmentId = SelectedAssignmentId.Value, FromUserId = userId, ToUserId = ToUserId.Value };

        // Persist creation-time warnings so the approver sees the same conflicts the requester
        // acknowledged. JSON serialization keeps the structured payload (Key, Message, Detail)
        // intact for re-rendering in the approval queue. Null when clean — saves a few bytes
        // and signals "no concerns" unambiguously to the approver UI.
        if (busyValidation.Warnings.Count > 0)
        {
            swapRequest.WarningsAtCreation = System.Text.Json.JsonSerializer.Serialize(busyValidation.Warnings);
        }

        _db.SwapRequests.Add(swapRequest);
        await _db.SaveChangesAsync();

        await _auditLogService.LogAsync("SwapRequestCreated", "SwapRequest", swapRequest.Id,
            $"Created swap request for assignment {SelectedAssignmentId.Value} to user {ToUserId.Value} (warnings: {busyValidation.Warnings.Count})");

        // Notify the counterparty that a swap request awaits their response (actionable) — closes
        // the gap where the other employee was never told. (Approver-pool alert for swaps: future.)
        var requesterName = await _db.Users.IgnoreQueryFilters()
            .Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync() ?? string.Empty;
        await _notificationService.NotifyAsync(ToUserId.Value, NotificationType.SwapRequestSubmitted,
            ShiftManager.Services.Notifications.NotificationCategory.Swap,
            _localizer["Notif_SwapRequestReceivedTitle"].Value,
            string.Format(_localizer["Notif_SwapRequestReceivedMessage"].Value, requesterName),
            personallyActionable: true, relatedEntityId: swapRequest.Id, relatedEntityType: "SwapRequest");

        return RedirectToPage("/Requests/Index");
    }
}
