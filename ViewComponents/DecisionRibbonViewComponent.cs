using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.ViewModels;
using ShiftManager.Models.Support;
using System.Security.Claims;

namespace ShiftManager.ViewComponents;

/// <summary>
/// View component for the Decision Ribbon - a signature feature showing pending approvals.
/// Displays in the header for Managers, Directors, and Owners.
/// Never renders on /Owner/* routes regardless of user role.
/// </summary>
public class DecisionRibbonViewComponent : ViewComponent
{
    private readonly AppDbContext _db;

    public DecisionRibbonViewComponent(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        // Get current user ID from claims
        var claimsPrincipal = User as ClaimsPrincipal;
        if (claimsPrincipal?.Identity?.IsAuthenticated != true)
        {
            return Content(""); // No ribbon for unauthenticated users
        }

        var userIdClaim = claimsPrincipal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
        {
            return Content(""); // No ribbon for unauthenticated users
        }

        // Load user with role
        var user = await _db.Users.FindAsync(userId);
        if (user == null || user.Role == UserRole.Employee || user.Role == UserRole.Trainee)
        {
            return Content(""); // No ribbon for employees/trainees
        }

        int pendingCount = 0;

        // Managers see time-off requests only
        if (user.Role == UserRole.Manager)
        {
            pendingCount = await _db.TimeOffRequests
                .Where(r => r.CompanyId == user.CompanyId && r.Status == RequestStatus.Pending)
                .CountAsync();
        }
        // Directors and Owners see all pending requests
        else if (user.Role == UserRole.Director || user.Role == UserRole.Owner || user.Role == UserRole.Assigner)
        {
            // Count pending time-off requests
            var timeOffCount = await _db.TimeOffRequests
                .Where(r => r.CompanyId == user.CompanyId && r.Status == RequestStatus.Pending)
                .CountAsync();

            // Count pending swap requests
            var swapCount = await _db.SwapRequests
                .Where(r => r.CompanyId == user.CompanyId && r.Status == RequestStatus.Pending)
                .CountAsync();

            pendingCount = timeOffCount + swapCount;
        }

        var viewModel = new DecisionRibbonViewModel
        {
            PendingCount = pendingCount,
            UserRole = user.Role.ToString()
        };

        return View(viewModel);
    }
}
