using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Pages.Api.Calendar;

/// <summary>
/// Returns shift assignment history for a specific user or shift instance.
/// Pulls data from AuditLog entries related to shift assignments (QA Item 63 / I-02).
/// Used by calendar UI to show "what happened to my shifts" for support triage.
/// </summary>
// SECURITY-AUDITED: IgnoreQueryFilters() is SAFE — requires [Authorize]; re-scoped by caller's CompanyId to prevent cross-tenant access
[Authorize]
[IgnoreAntiforgeryToken]
public class ShiftHistoryModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IGrantService _grantService;
    private readonly IDirectorService _directorService;

    public ShiftHistoryModel(AppDbContext db, IGrantService grantService, IDirectorService directorService)
    {
        _db = db;
        _grantService = grantService;
        _directorService = directorService;
    }

    public async Task<IActionResult> OnGetAsync(int? userId, int? instanceId, int limit = 50)
    {
        if (!userId.HasValue && !instanceId.HasValue)
        {
            return new JsonResult(new { error = "userId or instanceId is required" })
            {
                StatusCode = 400
            };
        }

        // Enforce tenant isolation: get the current user's company
        var currentUserIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(currentUserIdStr) || !int.TryParse(currentUserIdStr, out var currentUserId))
        {
            return new JsonResult(new { error = "Unauthorized" }) { StatusCode = 401 };
        }

        var currentUser = await _db.Users.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(u => u.Id == currentUserId)
            .Select(u => new { u.CompanyId, u.Role })
            .FirstOrDefaultAsync();

        if (currentUser == null)
        {
            return new JsonResult(new { error = "Unauthorized" }) { StatusCode = 401 };
        }

        var query = _db.AuditLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(al => al.EntityType == "ShiftAssignment"
                || al.EntityType == "ShiftInstance"
                || al.EntityType == "Chore"
                || al.EntityType == "OnDuty");

        // Tenant scoping: scope audit logs based on user's access level
        var isAdmin = await _grantService.HasGrantAsync(currentUserId, "AdminAccess");
        if (!isAdmin)
        {
            var isDirector = await _grantService.HasGrantAsync(currentUserId, "DirectorHubAccess");
            if (isDirector)
            {
                // Directors see only their managed companies' logs (not all companies)
                var directorCompanyIds = await _directorService.GetDirectorCompanyIdsAsync(currentUserId);
                // Include their own company too
                if (!directorCompanyIds.Contains(currentUser.CompanyId))
                    directorCompanyIds.Add(currentUser.CompanyId);
                query = query.Where(al => directorCompanyIds.Contains(al.CompanyId));
            }
            else
            {
                // Regular users see only their own company's logs
                query = query.Where(al => al.CompanyId == currentUser.CompanyId);
            }
        }

        if (userId.HasValue)
        {
            // Show history for a specific user — actions performed on entities involving this user
            query = query.Where(al =>
                al.UserId == userId.Value
                || al.Description.Contains($"UserId={userId.Value}")
                || al.Description.Contains($"user {userId.Value}"));
        }

        if (instanceId.HasValue)
        {
            // Show history for a specific shift instance
            query = query.Where(al => al.EntityId == instanceId.Value);
        }

        var history = await query
            .OrderByDescending(al => al.Timestamp)
            .Take(limit)
            .Select(al => new
            {
                al.Id,
                al.Action,
                al.EntityType,
                al.EntityId,
                al.Description,
                al.Timestamp,
                al.UserDisplayName,
                ChangedBy = al.UserDisplayName
            })
            .ToListAsync();

        return new JsonResult(history);
    }
}
