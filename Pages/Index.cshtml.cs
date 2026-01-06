using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models.Support;
using System.Security.Claims;

namespace ShiftManager.Pages;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db)
    {
        _db = db;
    }

    // Dashboard metrics
    public int UpcomingShiftsCount { get; set; }
    public int PendingRequestsCount { get; set; }
    public int TeamMembersCount { get; set; }
    public int UnreadNotificationsCount { get; set; }
    public bool IsAdmin { get; set; }
    public UserRole UserRole { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime? NextShiftDate { get; set; }
    public string NextShiftType { get; set; } = string.Empty;

    public async Task OnGetAsync()
    {
        var claimsPrincipal = User as ClaimsPrincipal;
        if (claimsPrincipal?.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var userIdClaim = claimsPrincipal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
        {
            return;
        }

        var user = await _db.Users.FindAsync(userId);
        if (user == null)
        {
            return;
        }

        UserRole = user.Role;
        UserName = user.DisplayName;
        IsAdmin = user.Role == UserRole.Owner || user.Role == UserRole.Manager ||
                  user.Role == UserRole.Director || user.Role == UserRole.Assigner;

        var now = DateOnly.FromDateTime(DateTime.UtcNow);
        var weekFromNow = now.AddDays(7);

        // Load upcoming shifts count (next 7 days)
        if (IsAdmin)
        {
            // Admins see all team shifts
            UpcomingShiftsCount = await _db.ShiftAssignments
                .Include(sa => sa.ShiftInstance)
                .Where(sa => sa.CompanyId == user.CompanyId &&
                            sa.ShiftInstance!.WorkDate >= now &&
                            sa.ShiftInstance.WorkDate <= weekFromNow)
                .CountAsync();
        }
        else
        {
            // Employees see their own shifts
            UpcomingShiftsCount = await _db.ShiftAssignments
                .Include(sa => sa.ShiftInstance)
                .Where(sa => sa.UserId == userId &&
                            sa.ShiftInstance!.WorkDate >= now &&
                            sa.ShiftInstance.WorkDate <= weekFromNow)
                .CountAsync();

            // Get next shift details for employees
            var nextShift = await _db.ShiftAssignments
                .Include(sa => sa.ShiftInstance)
                .ThenInclude(si => si!.ShiftType)
                .Where(sa => sa.UserId == userId && sa.ShiftInstance!.WorkDate >= now)
                .OrderBy(sa => sa.ShiftInstance!.WorkDate)
                .FirstOrDefaultAsync();

            if (nextShift?.ShiftInstance != null)
            {
                NextShiftDate = nextShift.ShiftInstance.WorkDate.ToDateTime(TimeOnly.MinValue);
                NextShiftType = nextShift.ShiftInstance.ShiftType?.Name ?? "";
            }
        }

        // Load pending requests count
        if (IsAdmin)
        {
            // Admins see all pending requests for their company
            var timeOffCount = await _db.TimeOffRequests
                .Where(r => r.CompanyId == user.CompanyId && r.Status == RequestStatus.Pending)
                .CountAsync();

            var swapCount = await _db.SwapRequests
                .Where(r => r.CompanyId == user.CompanyId && r.Status == RequestStatus.Pending)
                .CountAsync();

            PendingRequestsCount = timeOffCount + swapCount;
        }
        else
        {
            // Employees see their own pending requests
            var timeOffCount = await _db.TimeOffRequests
                .Where(r => r.UserId == userId && r.Status == RequestStatus.Pending)
                .CountAsync();

            var swapCount = await _db.SwapRequests
                .Where(r => r.FromUserId == userId && r.Status == RequestStatus.Pending)
                .CountAsync();

            PendingRequestsCount = timeOffCount + swapCount;
        }

        // Load team members count
        TeamMembersCount = await _db.Users
            .Where(u => u.CompanyId == user.CompanyId)
            .CountAsync();

        // Load unread notifications count
        UnreadNotificationsCount = await _db.UserNotifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .CountAsync();
    }
}
