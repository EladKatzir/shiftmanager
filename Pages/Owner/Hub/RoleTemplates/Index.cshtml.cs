using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Pages.Owner.Hub.RoleTemplates;

/// <summary>
/// Lists all role templates with user/grant counts.
/// </summary>
// SECURITY-AUDITED: IgnoreQueryFilters() used for cross-tenant user counting — Owner page requires Grant:AdminAccess
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

    public List<RoleTemplateListItem> Templates { get; set; } = new();
    public int TotalUsers { get; set; }
    public int TotalTemplates { get; set; }
    public int SystemTemplates { get; set; }
    public int CustomTemplates { get; set; }

    public async Task OnGetAsync()
    {
        try
        {
            var templates = await _db.RoleTemplates
                .Include(rt => rt.AutoGrants)
                .Include(rt => rt.JobTypeLabels)
                .OrderBy(rt => rt.SortOrder)
                .ThenBy(rt => rt.Key)
                .ToListAsync();

            // Count users per template (cross-tenant)
            var userCounts = await _db.Users
                .IgnoreQueryFilters()
                .Where(u => u.IsActive && u.RoleTemplateId != null)
                .GroupBy(u => u.RoleTemplateId!.Value)
                .Select(g => new { TemplateId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.TemplateId, x => x.Count);

            Templates = templates.Select(rt => new RoleTemplateListItem
            {
                Id = rt.Id,
                Key = rt.Key,
                NameKey = rt.NameKey,
                DescriptionKey = rt.DescriptionKey,
                ScopeLevel = rt.ScopeLevel,
                DerivedUserRole = rt.DerivedUserRole,
                DisplayNameEN = rt.DisplayNameEN,
                DisplayNameHE = rt.DisplayNameHE,
                IsSystem = rt.IsSystem,
                IsActive = rt.IsActive,
                CanBeAssignedByDefault = rt.CanBeAssignedByDefault,
                IsVisibleInSignup = rt.IsVisibleInSignup,
                SortOrder = rt.SortOrder,
                GrantCount = rt.AutoGrants.Count,
                UserCount = userCounts.GetValueOrDefault(rt.Id, 0),
                JobTypeLabelCount = rt.JobTypeLabels.Count
            }).ToList();

            TotalUsers = userCounts.Values.Sum();
            TotalTemplates = templates.Count;
            SystemTemplates = templates.Count(t => t.IsSystem);
            CustomTemplates = templates.Count(t => !t.IsSystem);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading role templates");
        }
    }

    public class RoleTemplateListItem
    {
        public int Id { get; set; }
        public string Key { get; set; } = string.Empty;
        public string NameKey { get; set; } = string.Empty;
        public string DescriptionKey { get; set; } = string.Empty;
        public RoleScopeLevel ScopeLevel { get; set; }
        public UserRole? DerivedUserRole { get; set; }
        public string? DisplayNameEN { get; set; }
        public string? DisplayNameHE { get; set; }
        public bool IsSystem { get; set; }
        public bool IsActive { get; set; }
        public bool CanBeAssignedByDefault { get; set; }
        public bool IsVisibleInSignup { get; set; }
        public int SortOrder { get; set; }
        public int GrantCount { get; set; }
        public int UserCount { get; set; }
        public int JobTypeLabelCount { get; set; }
    }
}
