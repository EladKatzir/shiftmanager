using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.ViewModels;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using System.Security.Claims;

namespace ShiftManager.ViewComponents;

/// <summary>
/// View component for the Decision Ribbon - a signature feature showing pending approvals.
/// Phase 2: Uses grant-based checks instead of role-based checks.
/// Displays for any user who has ApproveVacations or ApproveSwaps grants.
/// Never renders on /Owner/* routes regardless of user role.
/// </summary>
public class DecisionRibbonViewComponent : ViewComponent
{
    private readonly AppDbContext _db;
    private readonly IGrantService _grantService;

    public DecisionRibbonViewComponent(AppDbContext db, IGrantService grantService)
    {
        _db = db;
        _grantService = grantService;
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

        // Load user
        var user = await _db.Users.FindAsync(userId);
        if (user == null)
        {
            return Content("");
        }

        // Phase 2: Check grants instead of roles
        bool canApproveVacations = await _grantService.HasGrantAsync(userId, "ApproveVacations");
        bool canApproveSwaps = await _grantService.HasGrantAsync(userId, "ApproveSwaps");

        if (!canApproveVacations && !canApproveSwaps)
        {
            return Content(""); // No ribbon for users without approval grants
        }

        int pendingCount = 0;

        // Count pending time-off requests in accessible companies
        if (canApproveVacations)
        {
            var approveCompanyIds = await _grantService.GetAccessibleCompanyIdsForGrantAsync(userId, "ApproveVacations");
            if (approveCompanyIds.Any())
            {
                pendingCount += await _db.TimeOffRequests
                    .Where(r => approveCompanyIds.Contains(r.CompanyId) && r.Status == RequestStatus.Pending)
                    .CountAsync();
            }
        }

        // Count pending swap requests in accessible companies
        if (canApproveSwaps)
        {
            var swapCompanyIds = await _grantService.GetAccessibleCompanyIdsForGrantAsync(userId, "ApproveSwaps");
            if (swapCompanyIds.Any())
            {
                pendingCount += await _db.SwapRequests
                    .Where(r => swapCompanyIds.Contains(r.CompanyId) && r.Status == RequestStatus.Pending)
                    .CountAsync();
            }
        }

        var viewModel = new DecisionRibbonViewModel
        {
            PendingCount = pendingCount,
            UserRole = user.Role.ToString()
        };

        return View(viewModel);
    }
}
