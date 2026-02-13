using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Resources;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.Requests;

[Authorize(Policy = "IsManagerOrAdmin")]
public class IndexModel : LocalizedPageModel
{
    private readonly AppDbContext _db;
    private readonly IConflictChecker _checker;
    private readonly INotificationService _notificationService;
    private readonly ITraineeService _traineeService;
    private readonly ILogger<IndexModel> _logger;
    private readonly IDirectorService _directorService;

    public IndexModel(
        IStringLocalizer<SharedResources> localizer,
        AppDbContext db,
        IConflictChecker checker,
        INotificationService notificationService,
        ITraineeService traineeService,
        ILogger<IndexModel> logger,
        IDirectorService directorService)
        : base(localizer)
    {
        _db = db;
        _checker = checker;
        _notificationService = notificationService;
        _traineeService = traineeService;
        _logger = logger;
        _directorService = directorService;
    }

    public record TimeOffVM(int Id, string UserName, DateOnly StartDate, DateOnly EndDate, string? Reason);
    public List<TimeOffVM> TimeOff { get; set; } = new();

    public record SwapVM(int Id, string FromUser, string When, string ToUser);
    public List<SwapVM> Swaps { get; set; } = new();

    // ✅ Phase 18: Approved Time-Off (consolidated from Admin/TimeOff page)
    public record ApprovedTimeOffVM(int Id, string UserName, DateOnly StartDate, DateOnly EndDate,
                                   string? Reason, DateTime CreatedAt, DateTime ApprovedAt);
    public List<ApprovedTimeOffVM> ApprovedTimeOffs { get; set; } = new();

    public string? Message { get; set; }

    public async Task OnGetAsync()
    {
        try
        {
            _logger.LogInformation("Loading admin requests page");

            // Phase 8.2.1: Get current user for company filtering (fixed to use NameIdentifier instead of Email)
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var currentUserId))
            {
                Error = _localizer["Error_UserNotAuthenticated"];
                return;
            }

            var currentUser = await _db.Users.FindAsync(currentUserId);
            if (currentUser == null)
            {
                Error = _localizer["Error_UserNotFound"];
                return;
            }

            // Phase 8.2.1: Determine accessible company IDs based on role
            List<int> accessibleCompanyIds;
            if (currentUser.Role == UserRole.Owner)
            {
                // Owner sees all companies
                // IgnoreQueryFilters: Owner needs ALL company IDs, not just their tenant
                accessibleCompanyIds = await _db.Users.IgnoreQueryFilters().Select(u => u.CompanyId).Distinct().ToListAsync();
            }
            else if (currentUser.Role == UserRole.Director)
            {
                // Director sees companies they manage
                accessibleCompanyIds = await _directorService.GetDirectorCompanyIdsAsync(currentUser.Id);
            }
            else
            {
                // Manager sees only their own company
                accessibleCompanyIds = new List<int> { currentUser.CompanyId };
            }

            // Phase 8.2.1: Load pending time-off requests with company filtering
            _logger.LogInformation("Loading pending time off requests");
            // IgnoreQueryFilters: accessibleCompanyIds already scoped — tenant filter breaks multi-company views
            var pendingTO = await (from r in _db.TimeOffRequests.IgnoreQueryFilters()
                                   join u in _db.Users.IgnoreQueryFilters() on r.UserId equals u.Id
                                   where r.Status == RequestStatus.Pending && accessibleCompanyIds.Contains(u.CompanyId)
                                   orderby r.CreatedAt
                                   select new TimeOffVM(r.Id, u.DisplayName, r.StartDate, r.EndDate, r.Reason)).ToListAsync();
            TimeOff = pendingTO;
            _logger.LogInformation("Loaded {Count} pending time off requests", TimeOff.Count);

            // Phase 8.2.1: Load pending swap requests with company filtering
            _logger.LogInformation("Loading pending swap requests");
            // IgnoreQueryFilters: all joined tables have tenant filters that break multi-company views
            var pendingSwaps = await (from s in _db.SwapRequests.IgnoreQueryFilters()
                                      join a in _db.ShiftAssignments.IgnoreQueryFilters() on s.FromAssignmentId equals a.Id
                                      join u1 in _db.Users.IgnoreQueryFilters() on a.UserId equals u1.Id
                                      join si in _db.ShiftInstances.IgnoreQueryFilters() on a.ShiftInstanceId equals si.Id
                                      join st in _db.ShiftTypes.IgnoreQueryFilters() on si.ShiftTypeId equals st.Id
                                      join u2 in _db.Users.IgnoreQueryFilters() on s.ToUserId equals u2.Id into toUserJoin
                                      from u2 in toUserJoin.DefaultIfEmpty()
                                      where s.Status == RequestStatus.Pending && accessibleCompanyIds.Contains(u1.CompanyId)
                                      orderby s.CreatedAt
                                      select new
                                      {
                                          s.Id,
                                          FromUser = u1.DisplayName,
                                          When = $"{si.WorkDate:yyyy-MM-dd} {st.Key}",
                                          ToUser = u2 != null ? u2.DisplayName : "Open Request"
                                      }).ToListAsync();

            Swaps = pendingSwaps.Select(x => new SwapVM(x.Id, x.FromUser, x.When, x.ToUser)).ToList();
            _logger.LogInformation("Loaded {Count} pending swap requests", Swaps.Count);

            // ✅ Phase 18: Load approved time-off requests
            // Phase 8.2.1: Simplified to reuse accessibleCompanyIds from above
            _logger.LogInformation("Loading approved time off requests");
            // IgnoreQueryFilters: same multi-company scope as pending queries above
            ApprovedTimeOffs = await (from r in _db.TimeOffRequests.IgnoreQueryFilters()
                                     join u in _db.Users.IgnoreQueryFilters() on r.UserId equals u.Id
                                     where r.Status == RequestStatus.Approved && accessibleCompanyIds.Contains(u.CompanyId)
                                     orderby r.StartDate descending
                                     select new ApprovedTimeOffVM(r.Id, u.DisplayName, r.StartDate, r.EndDate, r.Reason, r.CreatedAt, r.CreatedAt)).ToListAsync();

            _logger.LogInformation("Loaded {Count} approved time off requests", ApprovedTimeOffs.Count);

            _logger.LogInformation("Admin requests page loaded successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading admin requests page");
            Error = _localizer["Error_LoadingRequests"];
        }
    }

    public async Task<IActionResult> OnPostApproveTimeOffAsync(int id)
    {
        // ✅ SECURITY FIX: Input validation
        if (id <= 0)
        {
            _logger.LogWarning("Invalid time off request ID: {Id}", id);
            return RedirectToPage();
        }

        // ✅ SECURITY FIX: Validate authorization before approving request
        // IgnoreQueryFilters: request may be in a different company than current tenant
        var r = await _db.TimeOffRequests.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id);
        if (r == null)
        {
            _logger.LogWarning("Time off request {RequestId} not found", id);
            return RedirectToPage();
        }

        // Get current user and validate they have access to this request's company
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
        {
            _logger.LogError("Invalid or missing NameIdentifier claim");
            Error = _localizer["Error_AuthenticationError"];
            return RedirectToPage();
        }

        var currentUser = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == currentUserId);
        var hasAccess = await ValidateAccessToRequestAsync(currentUser!, r.CompanyId);
        if (!hasAccess)
        {
            _logger.LogWarning("SECURITY: User {UserId} ({Role}) attempted to approve time off request {RequestId} for unauthorized company {CompanyId}",
                currentUserId, currentUser!.Role, id, r.CompanyId);
            Error = _localizer["Error_NoPermissionApproveRequest"];
            await OnGetAsync();
            return Page();
        }

        // ✅ CONCURRENCY FIX: Check status is still Pending before approving
        if (r.Status != RequestStatus.Pending)
        {
            _logger.LogWarning("CONCURRENCY: User {UserId} attempted to approve time off request {RequestId} with status {Status} (expected Pending)",
                currentUserId, id, r.Status);
            Error = _localizer["Error_RequestAlreadyProcessed"];
            await OnGetAsync();
            return Page();
        }

        r.Status = RequestStatus.Approved;

        // Remove existing assignments in the approved window
        // IgnoreQueryFilters: assignments may be in a different company
        var assignments = await (from a in _db.ShiftAssignments.IgnoreQueryFilters()
                                 join si in _db.ShiftInstances.IgnoreQueryFilters() on a.ShiftInstanceId equals si.Id
                                 where a.UserId == r.UserId && si.WorkDate >= r.StartDate && si.WorkDate <= r.EndDate
                                 select a).ToListAsync();
        if (assignments.Any())
        {
            _db.ShiftAssignments.RemoveRange(assignments);
        }

        // Cancel trainee shadowing assignments if user is a trainee
        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == r.UserId);
        if (user != null && user.Role == UserRole.Trainee)
        {
            var startDate = r.StartDate.ToDateTime(TimeOnly.MinValue);
            var endDate = r.EndDate.ToDateTime(TimeOnly.MaxValue);
            await _traineeService.CancelShadowingForTimeOffAsync(r.UserId, startDate, endDate);
        }

        await _db.SaveChangesAsync();

        // Send notification to user
        await _notificationService.CreateTimeOffNotificationAsync(r.UserId, RequestStatus.Approved, r.StartDate, r.EndDate, r.Id);

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeclineTimeOffAsync(int id)
    {
        // ✅ SECURITY FIX: Input validation
        if (id <= 0)
        {
            _logger.LogWarning("Invalid time off request ID: {Id}", id);
            return RedirectToPage();
        }

        // ✅ SECURITY FIX: Validate authorization before declining request
        // IgnoreQueryFilters: request may be in a different company than current tenant
        var r = await _db.TimeOffRequests.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id);
        if (r == null)
        {
            _logger.LogWarning("Time off request {RequestId} not found", id);
            return RedirectToPage();
        }

        // Get current user and validate they have access to this request's company
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
        {
            _logger.LogError("Invalid or missing NameIdentifier claim");
            Error = _localizer["Error_AuthenticationError"];
            return RedirectToPage();
        }

        var currentUser = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == currentUserId);
        var hasAccess = await ValidateAccessToRequestAsync(currentUser!, r.CompanyId);
        if (!hasAccess)
        {
            _logger.LogWarning("SECURITY: User {UserId} ({Role}) attempted to decline time off request {RequestId} for unauthorized company {CompanyId}",
                currentUserId, currentUser!.Role, id, r.CompanyId);
            Error = _localizer["Error_NoPermissionDeclineRequest"];
            await OnGetAsync();
            return Page();
        }

        // ✅ CONCURRENCY FIX: Check status is still Pending before declining
        if (r.Status != RequestStatus.Pending)
        {
            _logger.LogWarning("CONCURRENCY: User {UserId} attempted to decline time off request {RequestId} with status {Status} (expected Pending)",
                currentUserId, id, r.Status);
            Error = _localizer["Error_RequestAlreadyProcessed"];
            await OnGetAsync();
            return Page();
        }

        r.Status = RequestStatus.Declined;
        await _db.SaveChangesAsync();

        // Send notification to user
        await _notificationService.CreateTimeOffNotificationAsync(r.UserId, RequestStatus.Declined, r.StartDate, r.EndDate, r.Id);

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostApproveSwapAsync(int id)
    {
        // ✅ SECURITY FIX: Input validation
        if (id <= 0)
        {
            _logger.LogWarning("Invalid swap request ID: {Id}", id);
            return RedirectToPage();
        }

        using var trx = await _db.Database.BeginTransactionAsync();

        // ✅ SECURITY FIX: Validate authorization before approving swap
        // IgnoreQueryFilters: request may be in a different company than current tenant
        var s = await _db.SwapRequests.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id);
        if (s == null)
        {
            _logger.LogWarning("Swap request {RequestId} not found", id);
            return RedirectToPage();
        }

        // Get current user and validate they have access to this request's company
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
        {
            _logger.LogError("Invalid or missing NameIdentifier claim");
            Error = _localizer["Error_AuthenticationError"];
            await trx.RollbackAsync();
            return RedirectToPage();
        }

        var currentUser = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == currentUserId);
        var hasAccess = await ValidateAccessToRequestAsync(currentUser!, s.CompanyId);
        if (!hasAccess)
        {
            _logger.LogWarning("SECURITY: User {UserId} ({Role}) attempted to approve swap request {RequestId} for unauthorized company {CompanyId}",
                currentUserId, currentUser!.Role, id, s.CompanyId);
            Error = _localizer["Error_NoPermissionApproveRequest"];
            await trx.RollbackAsync();
            await OnGetAsync();
            return Page();
        }

        // ✅ CONCURRENCY FIX: Check status is still Pending before approving
        if (s.Status != RequestStatus.Pending)
        {
            _logger.LogWarning("CONCURRENCY: User {UserId} attempted to approve swap request {RequestId} with status {Status} (expected Pending)",
                currentUserId, id, s.Status);
            Error = _localizer["Error_RequestAlreadyProcessed"];
            await trx.RollbackAsync();
            await OnGetAsync();
            return Page();
        }

        // IgnoreQueryFilters: these entities may belong to a different company than current tenant
        var assign = await _db.ShiftAssignments.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == s.FromAssignmentId);
        if (assign == null) { s.Status = RequestStatus.Declined; await _db.SaveChangesAsync(); await trx.CommitAsync(); return RedirectToPage(); }

        var si = await _db.ShiftInstances.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == assign.ShiftInstanceId);
        if (si == null) { s.Status = RequestStatus.Declined; await _db.SaveChangesAsync(); await trx.CommitAsync(); return RedirectToPage(); }

        var shiftType = await _db.ShiftTypes.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == si.ShiftTypeId);
        if (shiftType == null) { s.Status = RequestStatus.Declined; await _db.SaveChangesAsync(); await trx.CommitAsync(); return RedirectToPage(); }

        if (!s.ToUserId.HasValue) { s.Status = RequestStatus.Declined; await _db.SaveChangesAsync(); await trx.CommitAsync(); return RedirectToPage(); }

        var conflict = await _checker.CanAssignAsync(s.ToUserId.Value, si);
        if (!conflict.Allowed)
        {
            Error = _localizer["Error_CannotApproveSwap"] + ": " + string.Join(" ", conflict.Reasons);
            await trx.RollbackAsync();
            await OnGetAsync();
            return Page();
        }

        // Get original user for notification
        var originalUserId = assign.UserId;

        // Reassign
        assign.UserId = s.ToUserId;
        s.Status = RequestStatus.Approved;
        await _db.SaveChangesAsync();
        await trx.CommitAsync();

        // Send notification to original user (if there was one)
        if (originalUserId.HasValue)
        {
            var shiftInfo = $"{shiftType.Name} on {si.WorkDate:MMM dd, yyyy} ({shiftType.Start:HH:mm} - {shiftType.End:HH:mm})";
            await _notificationService.CreateSwapRequestNotificationAsync(originalUserId.Value, RequestStatus.Approved, shiftInfo, s.Id);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeclineSwapAsync(int id)
    {
        // ✅ SECURITY FIX: Input validation
        if (id <= 0)
        {
            _logger.LogWarning("Invalid swap request ID: {Id}", id);
            return RedirectToPage();
        }

        // ✅ SECURITY FIX: Validate authorization before declining swap
        // IgnoreQueryFilters: request may be in a different company than current tenant
        var s = await _db.SwapRequests.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id);
        if (s == null)
        {
            _logger.LogWarning("Swap request {RequestId} not found", id);
            return RedirectToPage();
        }

        // Get current user and validate they have access to this request's company
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var currentUserId))
        {
            _logger.LogError("Invalid or missing NameIdentifier claim");
            Error = _localizer["Error_AuthenticationError"];
            return RedirectToPage();
        }

        var currentUser = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == currentUserId);
        var hasAccess = await ValidateAccessToRequestAsync(currentUser!, s.CompanyId);
        if (!hasAccess)
        {
            _logger.LogWarning("SECURITY: User {UserId} ({Role}) attempted to decline swap request {RequestId} for unauthorized company {CompanyId}",
                currentUserId, currentUser!.Role, id, s.CompanyId);
            Error = _localizer["Error_NoPermissionDeclineRequest"];
            await OnGetAsync();
            return Page();
        }

        // ✅ CONCURRENCY FIX: Check status is still Pending before declining
        if (s.Status != RequestStatus.Pending)
        {
            _logger.LogWarning("CONCURRENCY: User {UserId} attempted to decline swap request {RequestId} with status {Status} (expected Pending)",
                currentUserId, id, s.Status);
            Error = _localizer["Error_RequestAlreadyProcessed"];
            await OnGetAsync();
            return Page();
        }

        // Get shift information for notification before declining
        // IgnoreQueryFilters: all joined entities may be in a different company
        var shiftInfo = await (from sr in _db.SwapRequests.IgnoreQueryFilters()
                              join assign in _db.ShiftAssignments.IgnoreQueryFilters() on sr.FromAssignmentId equals assign.Id
                              join si in _db.ShiftInstances.IgnoreQueryFilters() on assign.ShiftInstanceId equals si.Id
                              join st in _db.ShiftTypes.IgnoreQueryFilters() on si.ShiftTypeId equals st.Id
                              where sr.Id == id
                              select new { assign.UserId, ShiftInfo = $"{st.Name} on {si.WorkDate:MMM dd, yyyy} ({st.Start:HH:mm} - {st.End:HH:mm})" })
                              .FirstOrDefaultAsync();

        s.Status = RequestStatus.Declined;
        await _db.SaveChangesAsync();

        // Send notification to user (if there was one)
        if (shiftInfo != null && shiftInfo.UserId.HasValue)
        {
            await _notificationService.CreateSwapRequestNotificationAsync(shiftInfo.UserId.Value, RequestStatus.Declined, shiftInfo.ShiftInfo, s.Id);
        }

        return RedirectToPage();
    }

    // ✅ Phase 18: Delete approved time-off (consolidated from Admin/TimeOff page)
    public async Task<IActionResult> OnPostDeleteTimeOffAsync(int id)
    {
        try
        {
            _logger.LogInformation("Admin attempting to delete approved time-off request {RequestId}", id);

            // Get current user for validation
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var currentUserId))
            {
                _logger.LogError("Invalid or missing NameIdentifier claim");
                Error = _localizer["Error_AuthenticationError"];
                return RedirectToPage();
            }

            // IgnoreQueryFilters: Owner/Director may be viewing a different company
            var currentUser = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == currentUserId);
            if (currentUser == null)
            {
                Error = _localizer["Error_UserNotFound"];
                return RedirectToPage();
            }

            // Load the time-off request and user for validation
            // IgnoreQueryFilters: request may be in a different company than current tenant
            var request = await _db.TimeOffRequests
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(r => r.Id == id);

            if (request == null)
            {
                _logger.LogWarning("Time-off request {RequestId} not found", id);
                Error = _localizer["Error_TimeOffRequestNotFound"];
                await OnGetAsync();
                return Page();
            }

            var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == request.UserId);
            if (user == null)
            {
                Error = _localizer["Error_UserNotFound"];
                await OnGetAsync();
                return Page();
            }

            // Validate access to the request's company
            var hasAccess = await ValidateAccessToRequestAsync(currentUser, user.CompanyId);
            if (!hasAccess)
            {
                _logger.LogWarning("SECURITY: User {UserId} ({Role}) attempted to delete time-off {RequestId} for unauthorized company",
                    currentUserId, currentUser.Role, id);
                Error = _localizer["Error_NoPermissionDeleteTimeOff"];
                await OnGetAsync();
                return Page();
            }

            if (request.Status != RequestStatus.Approved)
            {
                _logger.LogWarning("Attempt to delete non-approved time-off request {RequestId} with status {Status}", id, request.Status);
                Error = _localizer["Error_CanOnlyDeleteApprovedTimeOff"];
                await OnGetAsync();
                return Page();
            }

            // Check if time-off period has started
            if (request.StartDate <= DateOnly.FromDateTime(DateTime.Today))
            {
                _logger.LogWarning("Attempt to delete time-off request {RequestId} that has already started", id);
                Error = _localizer["Error_CannotDeleteStartedTimeOff"];
                await OnGetAsync();
                return Page();
            }

            var userName = user.DisplayName;

            _logger.LogInformation("Deleting approved time-off request {RequestId} for user {UserName} ({StartDate} to {EndDate})",
                id, userName, request.StartDate, request.EndDate);

            // Remove the time-off request
            _db.TimeOffRequests.Remove(request);
            await _db.SaveChangesAsync();

            // Notify the user that their time-off was deleted
            await _notificationService.CreateTimeOffDeletedNotificationAsync(
                userId: request.UserId,
                startDate: request.StartDate,
                endDate: request.EndDate);

            _logger.LogInformation("Successfully deleted time-off request {RequestId} for user {UserName}", id, userName);
            Message = string.Format(_localizer["Success_TimeOffDeleted"], userName, request.StartDate.ToString("yyyy-MM-dd"), request.EndDate.ToString("yyyy-MM-dd"));

            return RedirectToPage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting time-off request {RequestId}", id);
            Error = _localizer["Error_DeletingTimeOff"];
            await OnGetAsync();
            return Page();
        }
    }

    /// <summary>
    /// Validates that the current user has access to manage requests for the specified company.
    /// </summary>
    private async Task<bool> ValidateAccessToRequestAsync(AppUser currentUser, int targetCompanyId)
    {
        if (currentUser.Role == UserRole.Owner)
        {
            return true; // Owner has access to all companies
        }
        else if (currentUser.Role == UserRole.Director)
        {
            var directorCompanyIds = await _directorService.GetDirectorCompanyIdsAsync(currentUser.Id);
            return directorCompanyIds.Contains(targetCompanyId);
        }
        else if (currentUser.Role == UserRole.Manager)
        {
            return currentUser.CompanyId == targetCompanyId;
        }

        return false; // Employees and trainees cannot manage requests
    }
}
