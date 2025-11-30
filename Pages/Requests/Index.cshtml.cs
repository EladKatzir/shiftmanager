using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.Pages.Requests;

[Authorize(Policy = "IsManagerOrAdmin")]
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IConflictChecker _checker;
    private readonly INotificationService _notificationService;
    private readonly ITraineeService _traineeService;
    private readonly ILogger<IndexModel> _logger;
    private readonly IDirectorService _directorService;

    public IndexModel(AppDbContext db, IConflictChecker checker, INotificationService notificationService, ITraineeService traineeService, ILogger<IndexModel> logger, IDirectorService directorService)
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

    public string? Error { get; set; }
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
                Error = "User not authenticated";
                return;
            }

            var currentUser = await _db.Users.FindAsync(currentUserId);
            if (currentUser == null)
            {
                Error = "User not found";
                return;
            }

            // Phase 8.2.1: Determine accessible company IDs based on role
            List<int> accessibleCompanyIds;
            if (currentUser.Role == UserRole.Owner)
            {
                // Owner sees all companies
                accessibleCompanyIds = await _db.Users.Select(u => u.CompanyId).Distinct().ToListAsync();
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
            var pendingTO = await (from r in _db.TimeOffRequests
                                   join u in _db.Users on r.UserId equals u.Id
                                   where r.Status == RequestStatus.Pending && accessibleCompanyIds.Contains(u.CompanyId)
                                   orderby r.CreatedAt
                                   select new TimeOffVM(r.Id, u.DisplayName, r.StartDate, r.EndDate, r.Reason)).ToListAsync();
            TimeOff = pendingTO;
            _logger.LogInformation("Loaded {Count} pending time off requests", TimeOff.Count);

            // Phase 8.2.1: Load pending swap requests with company filtering
            _logger.LogInformation("Loading pending swap requests");
            var pendingSwaps = await (from s in _db.SwapRequests
                                      join a in _db.ShiftAssignments on s.FromAssignmentId equals a.Id
                                      join u1 in _db.Users on a.UserId equals u1.Id
                                      join si in _db.ShiftInstances on a.ShiftInstanceId equals si.Id
                                      join st in _db.ShiftTypes on si.ShiftTypeId equals st.Id
                                      join u2 in _db.Users on s.ToUserId equals u2.Id
                                      where s.Status == RequestStatus.Pending && accessibleCompanyIds.Contains(u1.CompanyId)
                                      orderby s.CreatedAt
                                      select new
                                      {
                                          s.Id,
                                          FromUser = u1.DisplayName,
                                          When = $"{si.WorkDate:yyyy-MM-dd} {st.Key}",
                                          ToUser = u2.DisplayName
                                      }).ToListAsync();

            Swaps = pendingSwaps.Select(x => new SwapVM(x.Id, x.FromUser, x.When, x.ToUser)).ToList();
            _logger.LogInformation("Loaded {Count} pending swap requests", Swaps.Count);

            // ✅ Phase 18: Load approved time-off requests
            // Phase 8.2.1: Simplified to reuse accessibleCompanyIds from above
            _logger.LogInformation("Loading approved time off requests");
            ApprovedTimeOffs = await (from r in _db.TimeOffRequests
                                     join u in _db.Users on r.UserId equals u.Id
                                     where r.Status == RequestStatus.Approved && accessibleCompanyIds.Contains(u.CompanyId)
                                     orderby r.StartDate descending
                                     select new ApprovedTimeOffVM(r.Id, u.DisplayName, r.StartDate, r.EndDate, r.Reason, r.CreatedAt, r.CreatedAt)).ToListAsync();

            _logger.LogInformation("Loaded {Count} approved time off requests", ApprovedTimeOffs.Count);

            _logger.LogInformation("Admin requests page loaded successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading admin requests page");
            Error = "An error occurred while loading requests. Please try again.";
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
        var r = await _db.TimeOffRequests.FindAsync(id);
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
            Error = "Authentication error. Please log in again.";
            return RedirectToPage();
        }

        var currentUser = await _db.Users.FindAsync(currentUserId);
        var hasAccess = await ValidateAccessToRequestAsync(currentUser!, r.CompanyId);
        if (!hasAccess)
        {
            _logger.LogWarning("SECURITY: User {UserId} ({Role}) attempted to approve time off request {RequestId} for unauthorized company {CompanyId}",
                currentUserId, currentUser!.Role, id, r.CompanyId);
            Error = "You don't have permission to approve this request.";
            await OnGetAsync();
            return Page();
        }

        r.Status = RequestStatus.Approved;

        // Remove existing assignments in the approved window
        var assignments = await (from a in _db.ShiftAssignments
                                 join si in _db.ShiftInstances on a.ShiftInstanceId equals si.Id
                                 where a.UserId == r.UserId && si.WorkDate >= r.StartDate && si.WorkDate <= r.EndDate
                                 select a).ToListAsync();
        if (assignments.Any())
        {
            _db.ShiftAssignments.RemoveRange(assignments);
        }

        // Cancel trainee shadowing assignments if user is a trainee
        var user = await _db.Users.FindAsync(r.UserId);
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
        var r = await _db.TimeOffRequests.FindAsync(id);
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
            Error = "Authentication error. Please log in again.";
            return RedirectToPage();
        }

        var currentUser = await _db.Users.FindAsync(currentUserId);
        var hasAccess = await ValidateAccessToRequestAsync(currentUser!, r.CompanyId);
        if (!hasAccess)
        {
            _logger.LogWarning("SECURITY: User {UserId} ({Role}) attempted to decline time off request {RequestId} for unauthorized company {CompanyId}",
                currentUserId, currentUser!.Role, id, r.CompanyId);
            Error = "You don't have permission to decline this request.";
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
        var s = await _db.SwapRequests.FindAsync(id);
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
            Error = "Authentication error. Please log in again.";
            await trx.RollbackAsync();
            return RedirectToPage();
        }

        var currentUser = await _db.Users.FindAsync(currentUserId);
        var hasAccess = await ValidateAccessToRequestAsync(currentUser!, s.CompanyId);
        if (!hasAccess)
        {
            _logger.LogWarning("SECURITY: User {UserId} ({Role}) attempted to approve swap request {RequestId} for unauthorized company {CompanyId}",
                currentUserId, currentUser!.Role, id, s.CompanyId);
            Error = "You don't have permission to approve this request.";
            await trx.RollbackAsync();
            await OnGetAsync();
            return Page();
        }

        var assign = await _db.ShiftAssignments.FindAsync(s.FromAssignmentId);
        if (assign == null) { s.Status = RequestStatus.Declined; await _db.SaveChangesAsync(); return RedirectToPage(); }

        var si = await _db.ShiftInstances.FindAsync(assign.ShiftInstanceId);
        if (si == null) { s.Status = RequestStatus.Declined; await _db.SaveChangesAsync(); return RedirectToPage(); }

        var shiftType = await _db.ShiftTypes.FindAsync(si.ShiftTypeId);
        if (shiftType == null) { s.Status = RequestStatus.Declined; await _db.SaveChangesAsync(); return RedirectToPage(); }

        if (!s.ToUserId.HasValue) { s.Status = RequestStatus.Declined; await _db.SaveChangesAsync(); return RedirectToPage(); }

        var conflict = await _checker.CanAssignAsync(s.ToUserId.Value, si);
        if (!conflict.Allowed)
        {
            Error = "Cannot approve swap: " + string.Join(" ", conflict.Reasons);
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
        var s = await _db.SwapRequests.FindAsync(id);
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
            Error = "Authentication error. Please log in again.";
            return RedirectToPage();
        }

        var currentUser = await _db.Users.FindAsync(currentUserId);
        var hasAccess = await ValidateAccessToRequestAsync(currentUser!, s.CompanyId);
        if (!hasAccess)
        {
            _logger.LogWarning("SECURITY: User {UserId} ({Role}) attempted to decline swap request {RequestId} for unauthorized company {CompanyId}",
                currentUserId, currentUser!.Role, id, s.CompanyId);
            Error = "You don't have permission to decline this request.";
            await OnGetAsync();
            return Page();
        }

        // Get shift information for notification before declining
        var shiftInfo = await (from sr in _db.SwapRequests
                              join assign in _db.ShiftAssignments on sr.FromAssignmentId equals assign.Id
                              join si in _db.ShiftInstances on assign.ShiftInstanceId equals si.Id
                              join st in _db.ShiftTypes on si.ShiftTypeId equals st.Id
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
                Error = "Authentication error. Please log in again.";
                return RedirectToPage();
            }

            var currentUser = await _db.Users.FindAsync(currentUserId);
            if (currentUser == null)
            {
                Error = "User not found.";
                return RedirectToPage();
            }

            // Load the time-off request and user for validation
            var request = await _db.TimeOffRequests
                .FirstOrDefaultAsync(r => r.Id == id);

            if (request == null)
            {
                _logger.LogWarning("Time-off request {RequestId} not found", id);
                Error = "Time-off request not found.";
                await OnGetAsync();
                return Page();
            }

            var user = await _db.Users.FindAsync(request.UserId);
            if (user == null)
            {
                Error = "User not found.";
                await OnGetAsync();
                return Page();
            }

            // Validate access to the request's company
            var hasAccess = await ValidateAccessToRequestAsync(currentUser, user.CompanyId);
            if (!hasAccess)
            {
                _logger.LogWarning("SECURITY: User {UserId} ({Role}) attempted to delete time-off {RequestId} for unauthorized company",
                    currentUserId, currentUser.Role, id);
                Error = "You don't have permission to delete this time-off request.";
                await OnGetAsync();
                return Page();
            }

            if (request.Status != RequestStatus.Approved)
            {
                _logger.LogWarning("Attempt to delete non-approved time-off request {RequestId} with status {Status}", id, request.Status);
                Error = "Can only delete approved time-off requests.";
                await OnGetAsync();
                return Page();
            }

            // Check if time-off period has started
            if (request.StartDate <= DateOnly.FromDateTime(DateTime.Today))
            {
                _logger.LogWarning("Attempt to delete time-off request {RequestId} that has already started", id);
                Error = "Cannot delete time-off that has already started or is in the past.";
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
            Message = $"Time-off for {userName} ({request.StartDate:yyyy-MM-dd} to {request.EndDate:yyyy-MM-dd}) has been deleted.";

            return RedirectToPage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting time-off request {RequestId}", id);
            Error = "An error occurred while deleting the time-off request. Please try again.";
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
