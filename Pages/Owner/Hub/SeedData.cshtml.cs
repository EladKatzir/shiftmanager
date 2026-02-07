using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
using ShiftManager.Data.SeedData;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Pages.Owner.Hub;

/// <summary>
/// SeedData diagnostic page - Shows seed status, allows seeding, and displays diagnostic info.
/// </summary>
[Authorize(Policy = "Grant:AdminAccess")]
public class SeedDataModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ILogger<SeedDataModel> _logger;

    public SeedDataModel(AppDbContext db, ILogger<SeedDataModel> logger)
    {
        _db = db;
        _logger = logger;
    }

    // Status counts
    public int GrantTypeCount { get; set; }
    public int RoleTemplateCount { get; set; }
    public int RoleTemplateGrantCount { get; set; }
    public int ProjectCount { get; set; }
    public int AreaCount { get; set; }
    public int MoleculeCount { get; set; }
    public int JobTypeCount { get; set; }
    public int CompanyCount { get; set; }
    public int DepartmentCount { get; set; }
    public int ShiftGroupingCount { get; set; }
    public int FeatureFlagCount { get; set; }

    // Expected counts from seed data
    public int ExpectedGrantTypes { get; set; }
    public int ExpectedRoleTemplates { get; set; }
    public int ExpectedFeatureFlags { get; set; }

    // Diagnostic data
    public List<CompanyDiagnosticVM> Companies { get; set; } = new();
    public List<UserDiagnosticVM> Users { get; set; } = new();
    public List<AdminGrantVM> AdminGrants { get; set; } = new();

    // Messages
    [TempData]
    public string? Message { get; set; }
    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        await LoadStatusAsync();
    }

    public async Task<IActionResult> OnPostSeedAllAsync()
    {
        try
        {
            // 1. Grant Types
            await SeedGrantTypesInternalAsync();
            // 2. Role Templates
            await SeedRoleTemplatesInternalAsync();
            // 3. Organization
            await ShiftyOrganizationSeed.SeedAsync(_db);
            // 4. Feature Flags
            await SeedFeatureFlagsInternalAsync();

            Message = "All seed data applied successfully.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeding all data");
            ErrorMessage = $"Error during seeding: {ex.Message}";
        }

        await LoadStatusAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostSeedGrantTypesAsync()
    {
        try
        {
            var count = await SeedGrantTypesInternalAsync();
            Message = count > 0 ? $"Seeded {count} grant types." : "Grant types already exist, skipped.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeding grant types");
            ErrorMessage = $"Error: {ex.Message}";
        }

        await LoadStatusAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostSeedRoleTemplatesAsync()
    {
        try
        {
            var count = await SeedRoleTemplatesInternalAsync();
            Message = count > 0 ? $"Seeded {count} role templates with grants." : "Role templates already exist, skipped.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeding role templates");
            ErrorMessage = $"Error: {ex.Message}";
        }

        await LoadStatusAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostSeedOrganizationAsync()
    {
        try
        {
            var existed = await _db.Projects.AnyAsync(p => p.Name == "Shifty");
            if (!existed)
            {
                await ShiftyOrganizationSeed.SeedAsync(_db);
                Message = "Organization hierarchy seeded successfully.";
            }
            else
            {
                Message = "Organization already exists, skipped.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeding organization");
            ErrorMessage = $"Error: {ex.Message}";
        }

        await LoadStatusAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostSeedFeatureFlagsAsync()
    {
        try
        {
            var count = await SeedFeatureFlagsInternalAsync();
            Message = count > 0 ? $"Seeded {count} feature flags." : "Feature flags already exist, skipped.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeding feature flags");
            ErrorMessage = $"Error: {ex.Message}";
        }

        await LoadStatusAsync();
        return Page();
    }

    // === Internal seed methods ===

    private async Task<int> SeedGrantTypesInternalAsync()
    {
        var existing = await _db.GrantTypes.Select(gt => gt.Key).ToListAsync();
        var seedTypes = GrantTypeSeed.GetGrantTypes();
        var toAdd = seedTypes.Where(gt => !existing.Contains(gt.Key)).ToList();

        if (toAdd.Count > 0)
        {
            // Clear Ids so EF generates them
            foreach (var gt in toAdd) gt.Id = 0;
            _db.GrantTypes.AddRange(toAdd);
            await _db.SaveChangesAsync();
            _logger.LogInformation("Seeded {Count} grant types", toAdd.Count);
        }

        return toAdd.Count;
    }

    private async Task<int> SeedRoleTemplatesInternalAsync()
    {
        var existingTemplates = await _db.RoleTemplates.Select(rt => rt.Key).ToListAsync();
        var seedTemplates = RoleTemplateSeed.GetRoleTemplates();
        var templatesToAdd = seedTemplates.Where(rt => !existingTemplates.Contains(rt.Key)).ToList();

        if (templatesToAdd.Count > 0)
        {
            foreach (var rt in templatesToAdd) rt.Id = 0;
            _db.RoleTemplates.AddRange(templatesToAdd);
            await _db.SaveChangesAsync();
        }

        // Seed role template grants - match by Key since IDs may differ
        var dbTemplates = await _db.RoleTemplates.ToDictionaryAsync(rt => rt.Id, rt => rt);
        var dbGrantTypes = await _db.GrantTypes.ToDictionaryAsync(gt => gt.Id, gt => gt);
        var existingRtGrants = await _db.RoleTemplateGrants.ToListAsync();

        var seedGrants = RoleTemplateSeed.GetRoleTemplateGrants();
        var grantsAdded = 0;

        foreach (var sg in seedGrants)
        {
            // Check if this combination already exists
            var exists = existingRtGrants.Any(e =>
                e.RoleTemplateId == sg.RoleTemplateId &&
                e.GrantTypeId == sg.GrantTypeId);

            if (!exists && dbTemplates.ContainsKey(sg.RoleTemplateId) && dbGrantTypes.ContainsKey(sg.GrantTypeId))
            {
                _db.RoleTemplateGrants.Add(new RoleTemplateGrant
                {
                    RoleTemplateId = sg.RoleTemplateId,
                    GrantTypeId = sg.GrantTypeId,
                    CanOwn = sg.CanOwn,
                    CanGive = sg.CanGive,
                    ScopeMode = sg.ScopeMode
                });
                grantsAdded++;
            }
        }

        if (grantsAdded > 0)
        {
            await _db.SaveChangesAsync();
            _logger.LogInformation("Seeded {Count} role template grants", grantsAdded);
        }

        return templatesToAdd.Count;
    }

    private async Task<int> SeedFeatureFlagsInternalAsync()
    {
        var existingNames = await _db.FeatureFlags.Select(f => f.Name).ToListAsync();
        var seedFlags = FeatureFlagSeed.GetFeatureFlags();
        var toAdd = seedFlags.Where(f => !existingNames.Contains(f.Name)).ToList();

        if (toAdd.Count > 0)
        {
            _db.FeatureFlags.AddRange(toAdd);
            await _db.SaveChangesAsync();
            _logger.LogInformation("Seeded {Count} feature flags", toAdd.Count);
        }

        return toAdd.Count;
    }

    private async Task LoadStatusAsync()
    {
        try
        {
            // Counts
            GrantTypeCount = await _db.GrantTypes.CountAsync();
            RoleTemplateCount = await _db.RoleTemplates.CountAsync();
            RoleTemplateGrantCount = await _db.RoleTemplateGrants.CountAsync();
            ProjectCount = await _db.Projects.IgnoreQueryFilters().CountAsync();
            AreaCount = await _db.Areas.IgnoreQueryFilters().CountAsync();
            MoleculeCount = await _db.Molecules.IgnoreQueryFilters().CountAsync();
            JobTypeCount = await _db.JobTypes.IgnoreQueryFilters().CountAsync();
            CompanyCount = await _db.Companies.IgnoreQueryFilters().CountAsync();
            DepartmentCount = await _db.Departments.IgnoreQueryFilters().CountAsync();
            ShiftGroupingCount = await _db.ShiftGroupings.IgnoreQueryFilters().CountAsync();
            FeatureFlagCount = await _db.FeatureFlags.CountAsync();

            // Expected counts
            ExpectedGrantTypes = GrantTypeSeed.GetGrantTypes().Count;
            ExpectedRoleTemplates = RoleTemplateSeed.GetRoleTemplates().Count;
            ExpectedFeatureFlags = FeatureFlagSeed.GetFeatureFlags().Count;

            // Diagnostic: Companies
            Companies = await _db.Companies
                .IgnoreQueryFilters()
                .Select(c => new CompanyDiagnosticVM
                {
                    Id = c.Id,
                    Name = c.Name,
                    MoleculeId = c.MoleculeId,
                    MoleculeName = c.Molecule != null ? c.Molecule.Name : null,
                    IsOrphaned = c.MoleculeId == null
                })
                .OrderBy(c => c.MoleculeName)
                .ThenBy(c => c.Name)
                .ToListAsync();

            // Diagnostic: Users (first 50) — join Company manually since AppUser has no nav property
            var companyLookup = await _db.Companies
                .IgnoreQueryFilters()
                .ToDictionaryAsync(c => c.Id, c => new { c.Name, c.MoleculeId });

            Users = await _db.Users
                .IgnoreQueryFilters()
                .Take(50)
                .OrderBy(u => u.DisplayName)
                .Select(u => new UserDiagnosticVM
                {
                    Id = u.Id,
                    DisplayName = u.DisplayName,
                    CompanyId = u.CompanyId
                })
                .ToListAsync();

            // Enrich with company info
            foreach (var u in Users)
            {
                if (companyLookup.TryGetValue(u.CompanyId, out var company))
                {
                    u.CompanyName = company.Name;
                    u.MoleculeId = company.MoleculeId;
                    u.IsOrphaned = false;
                }
                else
                {
                    u.IsOrphaned = true;
                }
            }

            // Diagnostic: Admin grants (user ID 1)
            AdminGrants = await _db.Grants
                .IgnoreQueryFilters()
                .Where(g => g.UserId == 1)
                .Select(g => new AdminGrantVM
                {
                    Id = g.Id,
                    GrantTypeKey = g.GrantType.Key,
                    ProjectId = g.ProjectId,
                    AreaId = g.AreaId,
                    MoleculeId = g.MoleculeId,
                    CompanyId = g.CompanyId,
                    CanOwn = g.CanOwn
                })
                .OrderBy(g => g.GrantTypeKey)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading seed data status");
        }
    }

    // === View Models ===

    public class CompanyDiagnosticVM
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int? MoleculeId { get; set; }
        public string? MoleculeName { get; set; }
        public bool IsOrphaned { get; set; }
    }

    public class UserDiagnosticVM
    {
        public int Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public int CompanyId { get; set; }
        public string? CompanyName { get; set; }
        public int? MoleculeId { get; set; }
        public bool IsOrphaned { get; set; }
    }

    public class AdminGrantVM
    {
        public int Id { get; set; }
        public string GrantTypeKey { get; set; } = string.Empty;
        public int? ProjectId { get; set; }
        public int? AreaId { get; set; }
        public int? MoleculeId { get; set; }
        public int? CompanyId { get; set; }
        public bool CanOwn { get; set; }
    }
}
