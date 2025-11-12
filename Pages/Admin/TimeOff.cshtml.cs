using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Pages.Admin;

[Authorize(Policy = "IsManagerOrAdmin")]
public class TimeOffModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ICompanyContext _companyContext;
    private readonly INotificationService _notificationService;
    private readonly ILogger<TimeOffModel> _logger;

    public TimeOffModel(AppDbContext db, ICompanyContext companyContext, INotificationService notificationService, ILogger<TimeOffModel> logger)
    {
        _db = db;
        _companyContext = companyContext;
        _notificationService = notificationService;
        _logger = logger;
    }

    public record ApprovedTimeOffVM(int Id, string UserName, DateOnly StartDate, DateOnly EndDate, string? Reason, DateTime CreatedAt, DateTime ApprovedAt);
    public List<ApprovedTimeOffVM> ApprovedTimeOffs { get; set; } = new();

    public string? Message { get; set; }
    public string? Error { get; set; }

    public async Task OnGetAsync()
    {
        try
        {
            _logger.LogInformation("Loading approved time-off requests for admin management");

            var companyId = _companyContext.GetCompanyIdOrThrow();

            // Query filters automatically scope TimeOffRequests by CompanyId
            ApprovedTimeOffs = await (from r in _db.TimeOffRequests
                                     join u in _db.Users on r.UserId equals u.Id
                                     where r.Status == RequestStatus.Approved && u.CompanyId == companyId
                                     orderby r.StartDate descending
                                     select new ApprovedTimeOffVM(
                                         r.Id,
                                         u.DisplayName,
                                         r.StartDate,
                                         r.EndDate,
                                         r.Reason,
                                         r.CreatedAt,
                                         r.CreatedAt
                                     )).ToListAsync();

            _logger.LogInformation("Loaded {Count} approved time-off requests", ApprovedTimeOffs.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading approved time-off requests");
            Error = "An error occurred while loading time-off requests. Please try again.";
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        try
        {
            _logger.LogInformation("Admin attempting to delete approved time-off request {RequestId}", id);

            var companyId = _companyContext.GetCompanyIdOrThrow();

            // Important: Validate tenant ownership before deletion
            var request = await _db.TimeOffRequests
                .FirstOrDefaultAsync(r => r.Id == id && r.CompanyId == companyId);

            if (request == null)
            {
                _logger.LogWarning("Time-off request {RequestId} not found or not owned by company {CompanyId}", id, companyId);
                Error = "Time-off request not found or access denied.";
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

            var user = await _db.Users.FindAsync(request.UserId);
            var userName = user?.DisplayName ?? "Unknown User";

            _logger.LogInformation("Deleting approved time-off request {RequestId} for user {UserName} ({StartDate} to {EndDate})",
                id, userName, request.StartDate, request.EndDate);

            // Remove the time-off request
            _db.TimeOffRequests.Remove(request);

            // Note: We don't need to restore shift assignments as they were removed when the request was approved
            // If you want to restore assignments, you'd need to track what was removed or have a more complex system

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
}