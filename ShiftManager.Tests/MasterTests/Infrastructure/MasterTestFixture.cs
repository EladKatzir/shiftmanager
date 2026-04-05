using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShiftManager.Data;
using ShiftManager.Data.SeedData;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Tests.MasterTests.Infrastructure;

/// <summary>
/// Shared fixture for the MasterTests collection. Seeds the full Shifty organization
/// hierarchy, grant types, role templates, and test users into a single InMemory DB.
///
/// Since ShiftyOrganizationSeed.SeedAsync uses transactions (unsupported by InMemory),
/// we replicate the hierarchy seeding inline without transaction wrappers.
/// </summary>
[CollectionDefinition("MasterTests")]
public class MasterTestCollection : ICollectionFixture<MasterTestFixture> { }

public class MasterTestFixture : IAsyncLifetime
{
    private readonly DbContextOptions<AppDbContext> _options;

    public AppDbContext Db { get; private set; } = null!;

    // Lookup dictionaries populated during seed
    public Dictionary<string, Project> ProjectByName { get; } = new();
    public Dictionary<string, Area> AreaByName { get; } = new();
    public Dictionary<string, Molecule> MoleculeByName { get; } = new();
    public Dictionary<string, Company> CompanyByName { get; } = new();
    public Dictionary<string, JobType> JobTypeByName { get; } = new();
    public Dictionary<string, ShiftGrouping> ShiftGroupingByName { get; } = new();
    public Dictionary<string, RoleTemplate> RoleTemplateByKey { get; } = new();
    public Dictionary<string, GrantType> GrantTypeByKey { get; } = new();
    public Dictionary<string, AppUser> UserByEmail { get; } = new();

    public MasterTestFixture()
    {
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "MasterTestDb")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    public async Task InitializeAsync()
    {
        // No tenant resolver → no query filters (tests control filtering explicitly)
        Db = new AppDbContext(_options);

        await SeedHierarchyAsync();
        await SeedGrantTypesAsync();
        await SeedRoleTemplatesAsync();
        await SeedTestUsersAsync();
    }

    public Task DisposeAsync()
    {
        Db.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Creates a fresh AppDbContext sharing the same InMemory DB.
    /// Use when tests need an isolated context (e.g., change tracker isolation).
    /// </summary>
    public AppDbContext CreateDbContext(Services.ITenantResolver? tenantResolver = null)
    {
        return new AppDbContext(_options, tenantResolver);
    }

    // ================================================================
    // HIERARCHY SEED (mirrors ShiftyOrganizationSeed without transactions)
    // ================================================================

    private async Task SeedHierarchyAsync()
    {
        // --- Project ---
        var project = new Project { Name = "Shifty", DisplayName = "שיפטי", IsActive = true, CreatedAt = DateTime.UtcNow };
        Db.Projects.Add(project);
        await Db.SaveChangesAsync();
        ProjectByName[project.Name] = project;

        // --- Area ---
        var area = new Area { ProjectId = project.Id, Name = "190", DisplayName = "190", IsActive = true, CreatedAt = DateTime.UtcNow };
        Db.Areas.Add(area);
        await Db.SaveChangesAsync();
        AreaByName[area.Name] = area;

        // --- Job Types (area-scoped) ---
        var jobTypes = new List<JobType>
        {
            new() { AreaId = area.Id, Name = "Alhut", DisplayName = "אלחוט", Color = "#3b82f6", SortOrder = 1, IsWorkforceOnly = true },
            new() { AreaId = area.Id, Name = "BR", DisplayName = "ב\"ר", Color = "#10b981", SortOrder = 2 },
            new() { AreaId = area.Id, Name = "Text", DisplayName = "טקסט", Color = "#8b5cf6", SortOrder = 3, IsWorkforceOnly = true },
            new() { AreaId = area.Id, Name = "Hakam", DisplayName = "חק\"ם", Color = "#f59e0b", SortOrder = 4 }
        };
        Db.JobTypes.AddRange(jobTypes);
        await Db.SaveChangesAsync();
        foreach (var jt in jobTypes) JobTypeByName[jt.Name] = jt;

        var alhut = JobTypeByName["Alhut"];
        var br = JobTypeByName["BR"];
        var text = JobTypeByName["Text"];

        // --- Molecules ---
        var oren = new Molecule { AreaId = area.Id, Name = "Oren", DisplayName = "אורן", Type = MoleculeType.Workforce };
        var ella = new Molecule { AreaId = area.Id, Name = "Ella", DisplayName = "אלה", Type = MoleculeType.Workforce };
        var harava = new Molecule { AreaId = area.Id, Name = "Harava", DisplayName = "ערבה", Type = MoleculeType.Workforce };
        var shaked = new Molecule { AreaId = area.Id, Name = "Shaked", DisplayName = "שקד", Type = MoleculeType.Workforce };
        var gefen = new Molecule { AreaId = area.Id, Name = "Gefen", DisplayName = "גפן", Type = MoleculeType.Workforce };
        var shikma = new Molecule { AreaId = area.Id, Name = "Shikma", DisplayName = "שקמה", Type = MoleculeType.Tech };
        var system = new Molecule { AreaId = area.Id, Name = "System", DisplayName = "מערכת", Type = MoleculeType.System };

        Db.Molecules.AddRange(oren, ella, harava, shaked, gefen, shikma, system);
        await Db.SaveChangesAsync();
        foreach (var m in new[] { oren, ella, harava, shaked, gefen, shikma, system })
            MoleculeByName[m.Name] = m;

        // --- Companies ---
        var companies = new List<Company>
        {
            // Oren
            new() { Name = "Tzafona", DisplayName = "צפונה", NameHe = "צפונה", MoleculeId = oren.Id },
            new() { Name = "Hir", DisplayName = "חיר", NameHe = "חיר", MoleculeId = oren.Id },
            new() { Name = "Camps", DisplayName = "מחנות", NameHe = "מחנות", MoleculeId = oren.Id },
            new() { Name = "City", DisplayName = "העיר", NameHe = "העיר", MoleculeId = oren.Id },
            new() { Name = "Radio", DisplayName = "טקטי", NameHe = "טקטי", MoleculeId = oren.Id },
            // Ella
            new() { Name = "Hitazmut", DisplayName = "התעצמות", NameHe = "התעצמות", MoleculeId = ella.Id },
            new() { Name = "GAP", DisplayName = "גא\"פ", NameHe = "גא\"פ", MoleculeId = ella.Id },
            new() { Name = "Yeadim", DisplayName = "יעדים", NameHe = "יעדים", MoleculeId = ella.Id },
            // Harava
            new() { Name = "Element", DisplayName = "אלמנט", NameHe = "אלמנט", MoleculeId = harava.Id },
            // Shaked
            new() { Name = "Inside", DisplayName = "פנים", NameHe = "פנים", MoleculeId = shaked.Id },
            new() { Name = "Out", DisplayName = "חוץ", NameHe = "חוץ", MoleculeId = shaked.Id },
            // Gefen
            new() { Name = "Hamasa", DisplayName = "חמסה", NameHe = "חמסה", MoleculeId = gefen.Id },
            new() { Name = "Kabah", DisplayName = "קבה\"ח", NameHe = "קבה\"ח", MoleculeId = gefen.Id },
            new() { Name = "Matot", DisplayName = "מטות", NameHe = "מטות", MoleculeId = gefen.Id },
            // Shikma (Tech)
            new() { Name = "Yekev", DisplayName = "יקב", NameHe = "יקב", MoleculeId = shikma.Id },
            new() { Name = "Snir", DisplayName = "שניר", NameHe = "שניר", MoleculeId = shikma.Id },
            // System
            new() { Name = "SystemAdmins", DisplayName = "מנהלי מערכת", NameHe = "מנהלי מערכת", MoleculeId = system.Id },
        };
        Db.Companies.AddRange(companies);
        await Db.SaveChangesAsync();
        foreach (var c in companies) CompanyByName[c.Name] = c;

        // --- Shift Groupings (Oren: Tzafon=Tzafona+City, Darom=Camps+Hir, Tacti=Radio) ---
        var tzafonGrouping = new ShiftGrouping { MoleculeId = oren.Id, Name = "Tzafon", DisplayName = "צפון" };
        var daromGrouping = new ShiftGrouping { MoleculeId = oren.Id, Name = "Darom", DisplayName = "דרום" };
        var tactiGrouping = new ShiftGrouping { MoleculeId = oren.Id, Name = "Tacti", DisplayName = "טקטי" };
        // Gefen groupings
        var hamasaKabahGrouping = new ShiftGrouping { MoleculeId = gefen.Id, Name = "HamasaKabah", DisplayName = "חמסה+קבה\"ח" };
        var matotGrouping = new ShiftGrouping { MoleculeId = gefen.Id, Name = "Matot", DisplayName = "מטות" };

        Db.ShiftGroupings.AddRange(tzafonGrouping, daromGrouping, tactiGrouping, hamasaKabahGrouping, matotGrouping);
        await Db.SaveChangesAsync();

        ShiftGroupingByName["Tzafon"] = tzafonGrouping;
        ShiftGroupingByName["Darom"] = daromGrouping;
        ShiftGroupingByName["Tacti"] = tactiGrouping;
        ShiftGroupingByName["HamasaKabah"] = hamasaKabahGrouping;
        ShiftGroupingByName["Matot"] = matotGrouping;

        // Link companies to shift groupings
        Db.Set<ShiftGroupingCompany>().AddRange(
            new ShiftGroupingCompany { ShiftGroupingId = tzafonGrouping.Id, CompanyId = CompanyByName["Tzafona"].Id },
            new ShiftGroupingCompany { ShiftGroupingId = tzafonGrouping.Id, CompanyId = CompanyByName["City"].Id },
            new ShiftGroupingCompany { ShiftGroupingId = daromGrouping.Id, CompanyId = CompanyByName["Camps"].Id },
            new ShiftGroupingCompany { ShiftGroupingId = daromGrouping.Id, CompanyId = CompanyByName["Hir"].Id },
            new ShiftGroupingCompany { ShiftGroupingId = tactiGrouping.Id, CompanyId = CompanyByName["Radio"].Id },
            new ShiftGroupingCompany { ShiftGroupingId = hamasaKabahGrouping.Id, CompanyId = CompanyByName["Hamasa"].Id },
            new ShiftGroupingCompany { ShiftGroupingId = hamasaKabahGrouping.Id, CompanyId = CompanyByName["Kabah"].Id },
            new ShiftGroupingCompany { ShiftGroupingId = matotGrouping.Id, CompanyId = CompanyByName["Matot"].Id }
        );

        // Link job types to shift groupings
        Db.Set<ShiftGroupingJobType>().AddRange(
            // Oren: Alhut + Text for all 3 groupings
            new ShiftGroupingJobType { ShiftGroupingId = tzafonGrouping.Id, JobTypeId = alhut.Id },
            new ShiftGroupingJobType { ShiftGroupingId = tzafonGrouping.Id, JobTypeId = text.Id },
            new ShiftGroupingJobType { ShiftGroupingId = daromGrouping.Id, JobTypeId = alhut.Id },
            new ShiftGroupingJobType { ShiftGroupingId = daromGrouping.Id, JobTypeId = text.Id },
            new ShiftGroupingJobType { ShiftGroupingId = tactiGrouping.Id, JobTypeId = alhut.Id },
            new ShiftGroupingJobType { ShiftGroupingId = tactiGrouping.Id, JobTypeId = text.Id },
            // Gefen: Alhut + Text + BR
            new ShiftGroupingJobType { ShiftGroupingId = hamasaKabahGrouping.Id, JobTypeId = alhut.Id },
            new ShiftGroupingJobType { ShiftGroupingId = hamasaKabahGrouping.Id, JobTypeId = text.Id },
            new ShiftGroupingJobType { ShiftGroupingId = hamasaKabahGrouping.Id, JobTypeId = br.Id },
            new ShiftGroupingJobType { ShiftGroupingId = matotGrouping.Id, JobTypeId = alhut.Id },
            new ShiftGroupingJobType { ShiftGroupingId = matotGrouping.Id, JobTypeId = text.Id },
            new ShiftGroupingJobType { ShiftGroupingId = matotGrouping.Id, JobTypeId = br.Id }
        );

        await Db.SaveChangesAsync();

        // --- Area Settings ---
        Db.AreaSettings.Add(new AreaSettings
        {
            AreaId = area.Id,
            DefaultRestHours = 8,
            DefaultWeeklyCap = 56,
            UpdatedAt = DateTime.UtcNow
        });
        await Db.SaveChangesAsync();
    }

    // ================================================================
    // GRANT TYPES + ROLE TEMPLATES (use real seed data)
    // ================================================================

    private async Task SeedGrantTypesAsync()
    {
        var grantTypes = GrantTypeSeed.GetGrantTypes();
        Db.GrantTypes.AddRange(grantTypes);
        await Db.SaveChangesAsync();
        foreach (var gt in grantTypes) GrantTypeByKey[gt.Key] = gt;
    }

    private async Task SeedRoleTemplatesAsync()
    {
        var templates = RoleTemplateSeed.GetRoleTemplates();
        Db.RoleTemplates.AddRange(templates);
        await Db.SaveChangesAsync();
        foreach (var rt in templates) RoleTemplateByKey[rt.Key] = rt;

        // Seed RoleTemplateGrants (these reference RoleTemplates and GrantTypes by ID)
        var templateGrants = RoleTemplateSeed.GetRoleTemplateGrants();

        // Resolve sentinel JobType IDs to actual DB IDs
        foreach (var tg in templateGrants)
        {
            if (tg.TargetJobTypeId.HasValue && RoleTemplateSeed.JobTypeSentinelMap.ContainsKey(tg.TargetJobTypeId.Value))
            {
                var jobTypeName = RoleTemplateSeed.JobTypeSentinelMap[tg.TargetJobTypeId.Value];
                tg.TargetJobTypeId = JobTypeByName[jobTypeName].Id;
            }
        }

        Db.Set<RoleTemplateGrant>().AddRange(templateGrants);
        await Db.SaveChangesAsync();
    }

    // ================================================================
    // TEST USERS (representative subset: 5 companies × key templates)
    // ================================================================

    private async Task SeedTestUsersAsync()
    {
        // Mapping: (companyName, templateKey, jobTypeName?) → email
        var userSpecs = new (string Company, string Template, string? JobType)[]
        {
            // Oren/Tzafona — workforce molecule, core templates
            ("Tzafona", "Employee", "Alhut"),
            ("Tzafona", "Employee", "Text"),
            ("Tzafona", "Lead", "Alhut"),
            ("Tzafona", "BRDirector", "BR"),
            ("Tzafona", "Director", "Alhut"),
            ("Tzafona", "Assigner", null),

            // Ella/Hitazmut — second molecule
            ("Hitazmut", "Employee", "Alhut"),
            ("Hitazmut", "Lead", "Text"),
            ("Hitazmut", "MoleculeAdmin", null),

            // Harava/Element — small molecule
            ("Element", "Employee", "Alhut"),
            ("Element", "BRDirector", "BR"),

            // Gefen/Hamasa — grouped molecule
            ("Hamasa", "Employee", "Alhut"),
            ("Hamasa", "Lead", "Text"),

            // Shikma/Yekev — tech molecule
            ("Yekev", "Employee", null),
            ("Yekev", "DepartmentLead", null),

            // System — Owner
            ("SystemAdmins", "Owner", null),

            // Area-level roles
            ("Tzafona", "AreaAdmin", null),
        };

        var emptyHash = Array.Empty<byte>();
        int userCounter = 0;

        foreach (var (companyName, templateKey, jobTypeName) in userSpecs)
        {
            userCounter++;
            var company = CompanyByName[companyName];
            var template = RoleTemplateByKey[templateKey];
            int? jobTypeId = jobTypeName != null ? JobTypeByName[jobTypeName].Id : null;

            var email = $"master.{companyName.ToLower()}.{templateKey.ToLower()}@test.com";
            var user = new AppUser
            {
                CompanyId = company.Id,
                JobTypeId = jobTypeId,
                Email = email,
                DisplayName = $"Test {templateKey} ({companyName})",
                PasswordHash = emptyHash,
                PasswordSalt = emptyHash,
                IsActive = true,
                Role = template.DerivedUserRole ?? UserRole.Employee
            };
            Db.Users.Add(user);
            await Db.SaveChangesAsync();

            UserByEmail[email] = user;

            // Create UserRoleAssignment
            var molecule = MoleculeByName.Values.FirstOrDefault(m => m.Id == company.MoleculeId);
            var area = AreaByName.Values.First();
            var project = ProjectByName.Values.First();

            var roleAssignment = new UserRoleAssignment
            {
                UserId = user.Id,
                RoleTemplateId = template.Id,
                CompanyId = company.Id,
                MoleculeId = molecule?.Id,
                AreaId = area.Id,
                JobTypeId = jobTypeId,
                AssignedByUserId = user.Id, // self-assigned for test data
                AssignedAt = DateTime.UtcNow,
                IsActive = true
            };
            Db.UserRoleAssignments.Add(roleAssignment);
            await Db.SaveChangesAsync();

            // Propagate auto-grants directly (mirrors GrantService.ApplyAutoGrantsAsync logic)
            await PropagateAutoGrantsAsync(user, template, company, molecule!, area, project, jobTypeId);
        }
    }

    /// <summary>
    /// Directly creates Grant rows for a user based on their role template's RoleTemplateGrants.
    /// This mirrors GrantService.ApplyAutoGrantsAsync without needing the full service stack.
    /// </summary>
    private async Task PropagateAutoGrantsAsync(
        AppUser user, RoleTemplate template,
        Company company, Molecule molecule, Area area, Project project,
        int? jobTypeId)
    {
        var templateGrants = await Db.Set<RoleTemplateGrant>()
            .Where(tg => tg.RoleTemplateId == template.Id)
            .ToListAsync();

        foreach (var tg in templateGrants)
        {
            // Resolve JobTypeId
            int? resolvedJobTypeId = null;
            if (tg.TargetJobTypeId.HasValue)
                resolvedJobTypeId = tg.TargetJobTypeId;
            else if (tg.UseOwnJobType)
                resolvedJobTypeId = jobTypeId;

            // Determine effective scope based on ScopeMode
            var (scopeProjectId, scopeAreaId, scopeMoleculeId, scopeCompanyId) = tg.ScopeMode switch
            {
                GrantScopeMode.SameAsRole => (default(int?), default(int?), default(int?), company.Id as int?),
                GrantScopeMode.ExpandToMolecule => (default(int?), default(int?), molecule.Id as int?, default(int?)),
                GrantScopeMode.ExpandToArea => (default(int?), area.Id as int?, default(int?), default(int?)),
                GrantScopeMode.ExpandToProject => (project.Id as int?, default(int?), default(int?), default(int?)),
                _ => (default(int?), default(int?), default(int?), company.Id as int?)
            };

            // For SameAsRole with higher-scoped templates, adjust
            if (tg.ScopeMode == GrantScopeMode.SameAsRole)
            {
                switch (template.ScopeLevel)
                {
                    case RoleScopeLevel.Molecule:
                        scopeCompanyId = null;
                        scopeMoleculeId = molecule.Id;
                        break;
                    case RoleScopeLevel.MoleculeJobType:
                        scopeCompanyId = null;
                        scopeMoleculeId = molecule.Id;
                        break;
                    case RoleScopeLevel.Area:
                        scopeCompanyId = null;
                        scopeAreaId = area.Id;
                        break;
                    case RoleScopeLevel.Project:
                        scopeCompanyId = null;
                        scopeProjectId = project.Id;
                        break;
                    case RoleScopeLevel.Department:
                        // DepartmentLead: keep company-scoped for now (no department in test data)
                        break;
                }
            }

            Db.Grants.Add(new Grant
            {
                UserId = user.Id,
                GrantTypeId = tg.GrantTypeId,
                ProjectId = scopeProjectId,
                AreaId = scopeAreaId,
                MoleculeId = scopeMoleculeId,
                CompanyId = scopeCompanyId,
                JobTypeId = resolvedJobTypeId,
                CanOwn = tg.CanOwn,
                CanGive = tg.CanGive,
                GrantedAt = DateTime.UtcNow,
                IsAutoGrant = true,
                Notes = $"Auto-granted from role: {template.Key}"
            });
        }

        await Db.SaveChangesAsync();
    }
}
