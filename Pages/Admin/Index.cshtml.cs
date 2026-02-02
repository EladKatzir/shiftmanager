using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Services;

namespace ShiftManager.Pages.Admin;

/// <summary>
/// Admin Hub - Central dashboard for management and configuration.
/// ✅ P2-1: Unified entry point for all administrative tasks.
/// Visible to Manager, Director, and Owner with role-based sections.
/// </summary>
[Authorize(Policy = "IsManagerOrAdmin")]
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ITenantResolver _tenantResolver;
    private readonly IDirectorService? _directorService;

    public IndexModel(
        AppDbContext db,
        ITenantResolver tenantResolver,
        IDirectorService? directorService = null)
    {
        _db = db;
        _tenantResolver = tenantResolver;
        _directorService = directorService;
    }

    // Role checks for stats calculation
    public bool IsOwner => User.IsInRole("Owner");
    public bool IsDirector => User.IsInRole("Director");
    public bool IsManager => User.IsInRole("Manager");

    // Stats
    public int TotalUsers { get; set; }
    public int TotalShifts { get; set; }
    public int TotalChores { get; set; }
    public int TotalAssignments { get; set; }

    public async Task OnGetAsync()
    {
        try
        {
            if (IsOwner)
            {
                // Owner sees all companies
                TotalUsers = await _db.Users.CountAsync(u => u.IsActive);
                TotalShifts = await _db.ShiftInstances.CountAsync();
                TotalChores = await _db.Chores.CountAsync();
                TotalAssignments = await _db.ShiftAssignments.CountAsync();
            }
            else if (IsDirector && _directorService != null)
            {
                // Director sees assigned companies
                var companyIds = await _directorService.GetDirectorCompanyIdsAsync();
                TotalUsers = await _db.Users.CountAsync(u => u.IsActive && companyIds.Contains(u.CompanyId));
                TotalShifts = await _db.ShiftInstances.CountAsync(si => companyIds.Contains(si.CompanyId));
                TotalChores = await _db.Chores.CountAsync(c => companyIds.Contains(c.CompanyId));
                TotalAssignments = await _db.ShiftAssignments.CountAsync(sa => companyIds.Contains(sa.CompanyId));
            }
            else
            {
                // Manager sees own company (query filters apply automatically)
                TotalUsers = await _db.Users.CountAsync(u => u.IsActive);
                TotalShifts = await _db.ShiftInstances.CountAsync();
                TotalChores = await _db.Chores.CountAsync();
                TotalAssignments = await _db.ShiftAssignments.CountAsync();
            }
        }
        catch
        {
            // Fallback to zero if any query fails
            TotalUsers = 0;
            TotalShifts = 0;
            TotalChores = 0;
            TotalAssignments = 0;
        }
    }
}
