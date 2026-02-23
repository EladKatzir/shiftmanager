using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Services;
using ShiftManager.Data.SeedData;
using System.Security.Claims;

namespace ShiftManager.Pages.Admin;

/// <summary>
/// Admin Hub - Central dashboard for management and configuration.
/// ✅ P2-1: Unified entry point for all administrative tasks.
/// Visible to users with AccessAdminNavigation grant.
/// </summary>
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — requires Grant:ManagerHomeAccess policy;
// cross-company queries scoped by accessible companyIds from caller's Director hierarchy; aggregate counts only
[Authorize(Policy = "Grant:ManagerHomeAccess")]
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ITenantResolver _tenantResolver;
    private readonly IDirectorService? _directorService;
    private readonly IGrantService _grantService;
    private readonly IFeatureFlagService _featureFlagService;

    public IndexModel(
        AppDbContext db,
        ITenantResolver tenantResolver,
        IGrantService grantService,
        IFeatureFlagService featureFlagService,
        IDirectorService? directorService = null)
    {
        _db = db;
        _tenantResolver = tenantResolver;
        _grantService = grantService;
        _featureFlagService = featureFlagService;
        _directorService = directorService;
    }

    // Grant-based access flags (set in OnGetAsync)
    public bool IsOwner { get; set; }
    public bool IsDirector { get; set; }
    public bool IsManager { get; set; }

    // Grant-based access for hub card visibility
    public bool HasViewHierarchy { get; set; }
    public bool HasEditCompany { get; set; }
    public bool HasManageJobTypes { get; set; }
    public bool HasManageDepartments { get; set; }
    public bool HasViewSettings { get; set; }
    public bool HasManageAnnouncements { get; set; }
    public bool HasSystemConfiguration { get; set; }
    public bool HasManageOnDuty { get; set; }

    // Feature flag states for conditional hub cards
    public bool DutyRotationEnabled { get; set; }
    public bool SetupTasksEnabled { get; set; }
    public bool VacationApprovalEnabled { get; set; }

    // Stats
    public int TotalUsers { get; set; }
    public int TotalShifts { get; set; }
    public int TotalChores { get; set; }
    public int TotalAssignments { get; set; }

    public async Task OnGetAsync()
    {
        // Get user ID for grant checks
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            return;
        }

        // Grant-based access checks (never use User.IsInRole)
        IsOwner = await _grantService.HasGrantAsync(userId, "AdminAccess");
        IsDirector = await _grantService.HasGrantAsync(userId, "DirectorHubAccess");
        IsManager = await _grantService.HasGrantAsync(userId, "AccessAdminNavigation") && !IsDirector && !IsOwner;

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
                // IgnoreQueryFilters: Director manages multiple companies — tenant filter restricts to home company
                var companyIds = await _directorService.GetDirectorCompanyIdsAsync();
                TotalUsers = await _db.Users.IgnoreQueryFilters().CountAsync(u => u.IsActive && companyIds.Contains(u.CompanyId));
                TotalShifts = await _db.ShiftInstances.IgnoreQueryFilters().CountAsync(si => companyIds.Contains(si.CompanyId));
                TotalChores = await _db.Chores.IgnoreQueryFilters().CountAsync(c => companyIds.Contains(c.CompanyId));
                TotalAssignments = await _db.ShiftAssignments.IgnoreQueryFilters().CountAsync(sa => companyIds.Contains(sa.CompanyId));
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

        // Grant checks for card visibility (only check if we have a valid userId)
        if (userId > 0)
        {
            HasViewHierarchy = await _grantService.HasGrantAsync(userId, "ViewHierarchy");
            HasEditCompany = await _grantService.HasGrantAsync(userId, "EditCompany");
            HasManageJobTypes = await _grantService.HasGrantAsync(userId, "ManageJobTypes");
            HasManageDepartments = await _grantService.HasGrantAsync(userId, "ManageDepartments");
            HasViewSettings = await _grantService.HasGrantAsync(userId, "ViewSettings");
            HasManageAnnouncements = await _grantService.HasGrantAsync(userId, "ManageAnnouncements");
            HasSystemConfiguration = await _grantService.HasGrantAsync(userId, "SystemConfiguration");
            HasManageOnDuty = await _grantService.HasGrantAsync(userId, "ManageOnDuty");
        }

        // Feature flags for conditional card rendering (sync cache-only check, same as _Layout.cshtml)
        DutyRotationEnabled = _featureFlagService.IsEnabled(FeatureFlagSeed.Flags.DutyRotationEnabled);
        SetupTasksEnabled = _featureFlagService.IsEnabled(FeatureFlagSeed.Flags.SetupTasksEnabled);
        VacationApprovalEnabled = _featureFlagService.IsEnabled(FeatureFlagSeed.Flags.VacationApprovalEnabled);
    }
}
