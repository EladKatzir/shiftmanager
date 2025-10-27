using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Pages.Requests;

[Authorize(Policy = "IsManagerOrAdmin")]
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IConflictChecker _checker;
    private readonly INotificationService _notificationService;
    private readonly ITraineeService _traineeService;
    private readonly ILogger<IndexModel> _logger;
    public IndexModel(AppDbContext db, IConflictChecker checker, INotificationService notificationService, ITraineeService traineeService, ILogger<IndexModel> logger)
    { _db = db; _checker = checker; _notificationService = notificationService; _traineeService = traineeService; _logger = logger; }

    public record TimeOffVM(int Id, string UserName, DateOnly StartDate, DateOnly EndDate, string? Reason);
    public List<TimeOffVM> TimeOff { get; set; } = new();

    public record SwapVM(int Id, string FromUser, string When, string ToUser);
    public List<SwapVM> Swaps { get; set; } = new();

    public string? Error { get; set; }

    public async Task OnGetAsync()
    {
        try
        {
            _logger.LogInformation("Loading admin requests page");

            _logger.LogInformation("Loading pending time off requests");
            var pendingTO = await (from r in _db.TimeOffRequests
                                   join u in _db.Users on r.UserId equals u.Id
                                   where r.Status == RequestStatus.Pending
                                   orderby r.CreatedAt
                                   select new TimeOffVM(r.Id, u.DisplayName, r.StartDate, r.EndDate, r.Reason)).ToListAsync();
            TimeOff = pendingTO;
            _logger.LogInformation("Loaded {Count} pending time off requests", TimeOff.Count);

            _logger.LogInformation("Loading pending swap requests");
            var pendingSwaps = await (from s in _db.SwapRequests
                                      join a in _db.ShiftAssignments on s.FromAssignmentId equals a.Id
                                      join u1 in _db.Users on a.UserId equals u1.Id
                                      join si in _db.ShiftInstances on a.ShiftInstanceId equals si.Id
                                      join st in _db.ShiftTypes on si.ShiftTypeId equals st.Id
                                      join u2 in _db.Users on s.ToUserId equals u2.Id
                                      where s.Status == RequestStatus.Pending
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
        var r = await _db.TimeOffRequests.FindAsync(id);
        if (r == null) return RedirectToPage();

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
        var r = await _db.TimeOffRequests.FindAsync(id);
        if (r == null) return RedirectToPage();

        r.Status = RequestStatus.Declined;
        await _db.SaveChangesAsync();

        // Send notification to user
        await _notificationService.CreateTimeOffNotificationAsync(r.UserId, RequestStatus.Declined, r.StartDate, r.EndDate, r.Id);

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostApproveSwapAsync(int id)
    {
        using var trx = await _db.Database.BeginTransactionAsync();
        var s = await _db.SwapRequests.FindAsync(id);
        if (s == null) return RedirectToPage();

        var assign = await _db.ShiftAssignments.FindAsync(s.FromAssignmentId);
        if (assign == null) { s.Status = RequestStatus.Declined; await _db.SaveChangesAsync(); return RedirectToPage(); }

        var si = await _db.ShiftInstances.FindAsync(assign.ShiftInstanceId);
        if (si == null) { s.Status = RequestStatus.Declined; await _db.SaveChangesAsync(); return RedirectToPage(); }

        var shiftType = await _db.ShiftTypes.FindAsync(si.ShiftTypeId);
        if (shiftType == null) { s.Status = RequestStatus.Declined; await _db.SaveChangesAsync(); return RedirectToPage(); }

        var conflict = await _checker.CanAssignAsync(s.ToUserId, si);
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
        var s = await _db.SwapRequests.FindAsync(id);
        if (s == null) return RedirectToPage();

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
}
