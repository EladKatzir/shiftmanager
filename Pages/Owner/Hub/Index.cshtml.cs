using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Pages.Owner.Hub;

/// <summary>
/// OwnerHub - Consolidated dashboard for system-wide administration.
/// Provides 6 category cards: Hierarchy, People, Grants, Scheduling, Settings, Analytics.
/// </summary>
[Authorize(Policy = "Grant:AdminAccess")]
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(AppDbContext db, ILogger<IndexModel> logger)
    {
        _db = db;
        _logger = logger;
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
            TotalRoleTemplates = await _db.RoleTemplates.CountAsync();
            TotalGrantAssignments = await _db.Grants.IgnoreQueryFilters().CountAsync();

            // Scheduling counts
            TotalPrograms = await _db.ShiftPrograms.IgnoreQueryFilters().CountAsync();
            TotalBlueprints = await _db.ShiftTypes.IgnoreQueryFilters().CountAsync();
            TotalShiftGroupings = await _db.ShiftGroupings.IgnoreQueryFilters().CountAsync();

            // Settings status
            EmailConfigured = await _db.EmailConfigs.IgnoreQueryFilters().AnyAsync(e => e.Enabled);
            AdfsConfigured = await _db.GriffinConfigs.IgnoreQueryFilters().AnyAsync(g => g.Enabled);
            FeatureFlagsEnabled = await _db.Configs.IgnoreQueryFilters()
                .CountAsync(c => c.Key.StartsWith("Feature:") && c.Value == "true");

            // Analytics counts
            TotalAuditLogs = await _db.AuditLogs.IgnoreQueryFilters().CountAsync();
            var weekAgo = DateTime.UtcNow.AddDays(-7);
            RecentAuditLogs = await _db.AuditLogs.IgnoreQueryFilters().CountAsync(a => a.Timestamp >= weekAgo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading OwnerHub dashboard stats");
        }
    }
}
