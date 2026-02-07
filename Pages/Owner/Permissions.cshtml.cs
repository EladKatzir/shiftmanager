using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;

namespace ShiftManager.Pages.Owner;

/// <summary>
/// Owner Permissions Dashboard - Summary stats and links to grant management pages
/// </summary>
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — Owner page requires Grant:AdminAccess (all 107 grants)
[Authorize(Policy = "Grant:AdminAccess")]
public class PermissionsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<PermissionsModel> _logger;

    public PermissionsModel(AppDbContext db, ILogger<PermissionsModel> logger)
    {
        _db = db;
        _logger = logger;
    }

    // Stats
    public int TotalGrants { get; set; }
    public int TotalGrantTypes { get; set; }
    public int UsersWithGrants { get; set; }
    public int AutoGrants { get; set; }
    public int ManualGrants { get; set; }

    // Grant type breakdown
    public List<GrantTypeStat> GrantTypeStats { get; set; } = new();

    // Recent grant activity
    public List<RecentGrant> RecentGrants { get; set; } = new();

    public async Task OnGetAsync()
    {
        try
        {
            // Calculate stats
            TotalGrants = await _db.Grants.IgnoreQueryFilters().CountAsync();
            TotalGrantTypes = await _db.GrantTypes.IgnoreQueryFilters().Where(gt => gt.IsActive).CountAsync();
            UsersWithGrants = await _db.Grants.IgnoreQueryFilters().Select(g => g.UserId).Distinct().CountAsync();
            AutoGrants = await _db.Grants.IgnoreQueryFilters().Where(g => g.IsAutoGrant).CountAsync();
            ManualGrants = TotalGrants - AutoGrants;

            // Grant type breakdown - fetch grant types and counts separately for EF Core compatibility
            var grantTypes = await _db.GrantTypes
                .IgnoreQueryFilters()
                .Where(gt => gt.IsActive)
                .Select(gt => new { gt.Id, gt.NameKey, gt.Category })
                .ToListAsync();

            var grantCounts = await _db.Grants
                .IgnoreQueryFilters()
                .GroupBy(g => g.GrantTypeId)
                .Select(grp => new { GrantTypeId = grp.Key, Count = grp.Count() })
                .ToListAsync();

            var countLookup = grantCounts.ToDictionary(gc => gc.GrantTypeId, gc => gc.Count);

            GrantTypeStats = grantTypes
                .Select(gt => new GrantTypeStat
                {
                    Id = gt.Id,
                    NameKey = gt.NameKey,
                    Category = gt.Category.ToString(),
                    Count = countLookup.GetValueOrDefault(gt.Id, 0)
                })
                .Where(gs => gs.Count > 0)
                .OrderByDescending(gs => gs.Count)
                .Take(10)
                .ToList();

            // Recent grants (last 10)
            RecentGrants = await _db.Grants
                .IgnoreQueryFilters()
                .Include(g => g.User)
                .Include(g => g.GrantType)
                .Include(g => g.GrantedByUser)
                .OrderByDescending(g => g.GrantedAt)
                .Take(10)
                .Select(g => new RecentGrant
                {
                    UserName = g.User.DisplayName,
                    GrantTypeName = g.GrantType.NameKey,
                    GrantedAt = g.GrantedAt,
                    GrantedByName = g.GrantedByUser != null ? g.GrantedByUser.DisplayName : null,
                    IsAutoGrant = g.IsAutoGrant
                })
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading Permissions dashboard");
        }
    }

    public class GrantTypeStat
    {
        public int Id { get; set; }
        public string NameKey { get; set; } = "";
        public string Category { get; set; } = "";
        public int Count { get; set; }
    }

    public class RecentGrant
    {
        public string UserName { get; set; } = "";
        public string GrantTypeName { get; set; } = "";
        public DateTime GrantedAt { get; set; }
        public string? GrantedByName { get; set; }
        public bool IsAutoGrant { get; set; }
    }
}
