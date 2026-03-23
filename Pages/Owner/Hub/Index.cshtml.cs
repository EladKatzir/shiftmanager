using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Data.SeedData;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Pages.Owner.Hub;

/// <summary>
/// OwnerHub - Consolidated dashboard for system-wide administration.
/// Provides 7 category cards: Hierarchy, People, Grants, Scheduling, Settings, Analytics, Seed Data.
/// </summary>
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — OwnerHub requires Grant:AdminAccess (all 107 grants)
[Authorize(Policy = "Grant:AdminAccess")]
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<IndexModel> _logger;
    private readonly IRoleService _roleService;
    private readonly ICompanyCacheService _companyCacheService;
    private readonly IFeatureFlagService _featureFlagService;

    public IndexModel(AppDbContext db, ILogger<IndexModel> logger, IRoleService roleService, ICompanyCacheService companyCacheService, IFeatureFlagService featureFlagService)
    {
        _db = db;
        _logger = logger;
        _roleService = roleService;
        _companyCacheService = companyCacheService;
        _featureFlagService = featureFlagService;
    }

    // Hierarchy Stats
    public int TotalProjects { get; set; }
    public int TotalAreas { get; set; }
    public int TotalMolecules { get; set; }
    public int TotalCompanies { get; set; }

    // People Stats
    public int TotalUsers { get; set; }
    public int PendingJoinRequests { get; set; }
    public int ActiveUsers { get; set; }

    // Grants Stats
    public int TotalGrantTypes { get; set; }
    public int TotalRoleTemplates { get; set; }
    public int TotalGrantAssignments { get; set; }

    // Scheduling Stats
    public int TotalPrograms { get; set; }
    public int TotalBlueprints { get; set; }
    public int TotalShiftGroupings { get; set; }

    // Settings Stats
    public bool EmailConfigured { get; set; }
    public bool AdfsConfigured { get; set; }
    public int FeatureFlagsEnabled { get; set; }

    // Analytics Stats
    public int TotalAuditLogs { get; set; }
    public int RecentAuditLogs { get; set; }

    // Seed Data Stats
    public int TotalSeedEntities { get; set; }
    public bool SeedDataHealthy { get; set; }

    // Feature Flag Properties (for Company Configuration card)
    public bool DutyRotationEnabled { get; set; }
    public bool SetupTasksEnabled { get; set; }
    public bool VacationApprovalEnabled { get; set; }

    public async Task OnGetAsync()
    {
        try
        {
            // Hierarchy counts (use IgnoreQueryFilters to get global counts)
            TotalProjects = await _db.Projects.IgnoreQueryFilters().CountAsync();
            TotalAreas = await _db.Areas.IgnoreQueryFilters().CountAsync();
            TotalMolecules = await _db.Molecules.IgnoreQueryFilters().CountAsync();
            TotalCompanies = await _db.Companies.IgnoreQueryFilters().CountAsync();

            // People counts
            TotalUsers = await _db.Users.IgnoreQueryFilters().CountAsync();
            ActiveUsers = await _db.Users.IgnoreQueryFilters().CountAsync(u => u.IsActive);
            PendingJoinRequests = await _db.UserJoinRequests.IgnoreQueryFilters()
                .CountAsync(r => r.Status == JoinRequestStatus.Pending);

            // Grants counts
            TotalGrantTypes = await _db.GrantTypes.CountAsync();
            TotalRoleTemplates = await _roleService.GetActiveRoleTemplateCountAsync();
            TotalGrantAssignments = await _db.Grants.IgnoreQueryFilters().CountAsync();

            // Scheduling counts
            TotalPrograms = await _db.ShiftPrograms.IgnoreQueryFilters().CountAsync();
            TotalBlueprints = await _db.ShiftTypes.IgnoreQueryFilters().CountAsync();
            TotalShiftGroupings = await _db.ShiftGroupings.IgnoreQueryFilters().CountAsync();

            // Settings status
            EmailConfigured = await _db.EmailConfigs.IgnoreQueryFilters().AnyAsync(e => e.Enabled);
            AdfsConfigured = await _db.GriffinConfigs.IgnoreQueryFilters().AnyAsync(g => g.Enabled);
            // Feature flags are in the FeatureFlags table (not Configs).
            // Count globally-enabled flags (CompanyId == null && UserId == null) to avoid counting per-company overrides.
            FeatureFlagsEnabled = await _db.FeatureFlags.IgnoreQueryFilters()
                .CountAsync(f => f.IsEnabled && f.CompanyId == null && f.UserId == null);

            // Analytics counts
            TotalAuditLogs = await _db.AuditLogs.IgnoreQueryFilters().CountAsync();
            var weekAgo = DateTime.UtcNow.AddDays(-7);
            RecentAuditLogs = await _db.AuditLogs.IgnoreQueryFilters().CountAsync(a => a.Timestamp >= weekAgo);

            // Seed data stats
            var grantTypeCount = await _db.GrantTypes.CountAsync();
            var roleTemplateCount = await _roleService.GetRoleTemplateCountAsync();
            var featureFlagCount = await _db.FeatureFlags.CountAsync();
            TotalSeedEntities = grantTypeCount + roleTemplateCount + TotalProjects + featureFlagCount;
            SeedDataHealthy = !await _companyCacheService.HasOrphanedCompaniesAsync();

            // Feature flags for Company Configuration card
            DutyRotationEnabled = _featureFlagService.IsEnabled(FeatureFlagSeed.Flags.DutyRotationEnabled);
            SetupTasksEnabled = _featureFlagService.IsEnabled(FeatureFlagSeed.Flags.SetupTasksEnabled);
            VacationApprovalEnabled = _featureFlagService.IsEnabled(FeatureFlagSeed.Flags.VacationApprovalEnabled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading OwnerHub dashboard stats");
        }
    }
}
