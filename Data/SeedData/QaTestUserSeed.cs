using Microsoft.EntityFrameworkCore;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Data.SeedData;

/// <summary>
/// Seeds test user accounts that match the qa-automation TEST_USERS definitions.
/// Only runs in Development/Test environments.
/// Idempotent — safe to call multiple times.
///
/// Creates ~45 test users across real seeded companies (Tzafona, Hir, Hitazmut, etc.)
/// plus QA-specific companies (QA-Alpha, QA-Beta) under a new QA molecule.
/// </summary>
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — dev-only test data seeding, guarded by IsTestEnvironment() check
public static class QaTestUserSeed
{
    private const string TEST_PASSWORD = "Test1234!";

    public static async Task SeedAsync(AppDbContext db, ILogger logger)
    {
        if (!IsTestEnvironment())
        {
            logger.LogInformation("[QaTestUserSeed] Skipping — not in Development/Test environment");
            return;
        }

        logger.LogInformation("[QaTestUserSeed] Starting QA test user seeding...");

        // ============================================================
        // 1. Resolve hierarchy entities (all must exist from ShiftyOrganizationSeed)
        // ============================================================
        var area = await db.Areas.FirstOrDefaultAsync(a => a.Name == "190");
        if (area == null)
        {
            logger.LogWarning("[QaTestUserSeed] Area '190' not found — ShiftyOrganizationSeed may not have run");
            return;
        }

        // Job types
        var jobTypes = await db.JobTypes.Where(j => j.AreaId == area.Id).ToListAsync();
        var alhut = jobTypes.FirstOrDefault(j => j.Name == "Alhut");
        var br = jobTypes.FirstOrDefault(j => j.Name == "BR");
        var text = jobTypes.FirstOrDefault(j => j.Name == "Text");
        var hakam = jobTypes.FirstOrDefault(j => j.Name == "Hakam");

        if (alhut == null || br == null || text == null || hakam == null)
        {
            logger.LogWarning("[QaTestUserSeed] Missing job types — expected Alhut, BR, Text, Hakam");
            return;
        }

        // Molecules
        var molecules = await db.Molecules.Where(m => m.AreaId == area.Id).ToListAsync();
        var oren = molecules.FirstOrDefault(m => m.Name == "Oren");
        var ella = molecules.FirstOrDefault(m => m.Name == "Ella");
        var harava = molecules.FirstOrDefault(m => m.Name == "Harava");
        var gefen = molecules.FirstOrDefault(m => m.Name == "Gefen");
        var shikma = molecules.FirstOrDefault(m => m.Name == "Shikma");
        var systemMol = molecules.FirstOrDefault(m => m.Name == "System");

        if (oren == null || ella == null || harava == null || gefen == null || shikma == null || systemMol == null)
        {
            logger.LogWarning("[QaTestUserSeed] Missing molecules — ShiftyOrganizationSeed incomplete");
            return;
        }

        // Companies (all from ShiftyOrganizationSeed)
        // Use first-wins for duplicate names (HQ companies share name "HQ" across molecules)
        var allCompanies = await db.Companies.IgnoreQueryFilters().ToListAsync();
        var companyMap = new Dictionary<string, Company>();
        foreach (var c in allCompanies)
        {
            companyMap.TryAdd(c.Name, c);
        }

        // Departments (for tech molecule)
        var departments = await db.Departments.Where(d => d.MoleculeId == shikma.Id).ToListAsync();
        var deptMap = departments.ToDictionary(d => d.Name, d => d);

        // Role templates
        var roleTemplates = await db.RoleTemplates.ToListAsync();
        var templateMap = roleTemplates.ToDictionary(rt => rt.Key, rt => rt);

        // ============================================================
        // 2. Create QA-specific companies (QA-Alpha, QA-Beta under a QA molecule)
        // ============================================================
        var qaMolecule = await EnsureQaMoleculeAsync(db, area.Id, logger);
        await EnsureQaCompaniesAsync(db, qaMolecule.Id, companyMap, logger);

        // Refresh company map after QA companies created
        allCompanies = await db.Companies.IgnoreQueryFilters().ToListAsync();
        companyMap = new Dictionary<string, Company>();
        foreach (var c in allCompanies)
        {
            companyMap.TryAdd(c.Name, c);
        }

        // ============================================================
        // 3. Create all test users
        // ============================================================
        var users = BuildTestUserDefinitions(companyMap, templateMap, jobTypes, deptMap);
        int created = 0, updated = 0, skipped = 0;

        foreach (var def in users)
        {
            var result = await EnsureUserAsync(db, def, logger);
            switch (result)
            {
                case "created": created++; break;
                case "updated": updated++; break;
                default: skipped++; break;
            }
        }

        // ============================================================
        // 4. Handle special cases
        // ============================================================
        await HandleSpecialCases(db, companyMap, logger);

        await db.SaveChangesAsync();
        logger.LogInformation(
            "[QaTestUserSeed] Done — created: {Created}, updated: {Updated}, skipped: {Skipped}",
            created, updated, skipped);
    }

    // ================================================================
    // User Definitions — matches qa-automation/helpers/production-qa-helpers.js TEST_USERS
    // ================================================================

    private static List<TestUserDef> BuildTestUserDefinitions(
        Dictionary<string, Company> companies,
        Dictionary<string, RoleTemplate> templates,
        List<JobType> jobTypes,
        Dictionary<string, Department> departments)
    {
        var alhut = jobTypes.FirstOrDefault(j => j.Name == "Alhut");
        var br = jobTypes.FirstOrDefault(j => j.Name == "BR");
        var text = jobTypes.FirstOrDefault(j => j.Name == "Text");
        var hakam = jobTypes.FirstOrDefault(j => j.Name == "Hakam");

        var defs = new List<TestUserDef>();

        // --- Owner ---
        // admin@local is created by Program.cs seeding, skip it here
        AddUser(defs, "owner2@test", "Owner 2", UserRole.Owner, "SystemAdmins", null, "Owner", companies, templates, alhut, br, text, hakam);

        // --- Directors ---
        AddUser(defs, "dir.alhut@test", "Dir Alhut", UserRole.Director, "Tzafona", "Alhut", "Director", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "dir.text@test", "Dir Text", UserRole.Director, "Hitazmut", "Text", "Director", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "dir.br@test", "Dir BR", UserRole.Director, "Hir", "BR", "BRDirector", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "dir.hakam@test", "Dir Hakam", UserRole.Director, "Yeadim", "Hakam", "Employee", companies, templates, alhut, br, text, hakam);

        // --- Managers / Leads ---
        AddUser(defs, "mgr.alhut.tz@test", "Mgr Alhut TZ", UserRole.Manager, "Tzafona", "Alhut", "Lead", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "mgr.text.tz@test", "Mgr Text TZ", UserRole.Manager, "Tzafona", "Text", "Lead", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "mgr.br.hir@test", "Mgr BR Hir", UserRole.Manager, "Hir", "BR", "BRDirector", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "mgr.hakam.ella@test", "Mgr Hakam Ella", UserRole.Manager, "Hitazmut", "Hakam", "Employee", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "moladmin.oren@test", "MolAdmin Oren", UserRole.Manager, "Tzafona", "Alhut", "MoleculeAdmin", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "moladmin.ella@test", "MolAdmin Ella", UserRole.Manager, "Hitazmut", "Text", "MoleculeAdmin", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "areaadmin@test", "Area Admin", UserRole.Manager, "Tzafona", null, "AreaAdmin", companies, templates, alhut, br, text, hakam);

        // Department lead (tech molecule - no company, uses department)
        AddDeptUser(defs, "deptlead.pie@test", "DeptLead Pie", UserRole.Manager, "DepartmentLead", "Pie", departments, templates);

        // --- Employees (Tzafona - all 4 job types) ---
        AddUser(defs, "emp.tz.alhut@test", "Emp TZ Alhut", UserRole.Employee, "Tzafona", "Alhut", "Employee", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "emp.tz.text@test", "Emp TZ Text", UserRole.Employee, "Tzafona", "Text", "Employee", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "emp.tz.br@test", "Emp TZ BR", UserRole.Employee, "Tzafona", "BR", "Employee", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "emp.tz.hakam@test", "Emp TZ Hakam", UserRole.Employee, "Tzafona", "Hakam", "Employee", companies, templates, alhut, br, text, hakam);

        // --- Employees (Hir - all 4 job types) ---
        AddUser(defs, "emp.hir.alhut@test", "Emp Hir Alhut", UserRole.Employee, "Hir", "Alhut", "Employee", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "emp.hir.text@test", "Emp Hir Text", UserRole.Employee, "Hir", "Text", "Employee", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "emp.hir.br@test", "Emp Hir BR", UserRole.Employee, "Hir", "BR", "Employee", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "emp.hir.hakam@test", "Emp Hir Hakam", UserRole.Employee, "Hir", "Hakam", "Employee", companies, templates, alhut, br, text, hakam);

        // --- Employees (Hitazmut - all 4 job types) ---
        AddUser(defs, "emp.hit.alhut@test", "Emp Hit Alhut", UserRole.Employee, "Hitazmut", "Alhut", "Employee", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "emp.hit.text@test", "Emp Hit Text", UserRole.Employee, "Hitazmut", "Text", "Employee", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "emp.hit.br@test", "Emp Hit BR", UserRole.Employee, "Hitazmut", "BR", "Employee", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "emp.hit.hakam@test", "Emp Hit Hakam", UserRole.Employee, "Hitazmut", "Hakam", "Employee", companies, templates, alhut, br, text, hakam);

        // --- Employees (Element - all 4 job types) ---
        AddUser(defs, "emp.elem.alhut@test", "Emp Elem Alhut", UserRole.Employee, "Element", "Alhut", "Employee", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "emp.elem.text@test", "Emp Elem Text", UserRole.Employee, "Element", "Text", "Employee", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "emp.elem.br@test", "Emp Elem BR", UserRole.Employee, "Element", "BR", "Employee", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "emp.elem.hakam@test", "Emp Elem Hakam", UserRole.Employee, "Element", "Hakam", "Employee", companies, templates, alhut, br, text, hakam);

        // --- Employees (QA-Alpha - all 4 job types) ---
        AddUser(defs, "emp.alpha.alhut@test", "Emp Alpha Alhut", UserRole.Employee, "QA-Alpha", "Alhut", "Employee", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "emp.alpha.text@test", "Emp Alpha Text", UserRole.Employee, "QA-Alpha", "Text", "Employee", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "emp.alpha.br@test", "Emp Alpha BR", UserRole.Employee, "QA-Alpha", "BR", "Employee", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "emp.alpha.hakam@test", "Emp Alpha Hakam", UserRole.Employee, "QA-Alpha", "Hakam", "Employee", companies, templates, alhut, br, text, hakam);

        // --- Employees (QA-Beta - all 4 job types) ---
        AddUser(defs, "emp.beta.alhut@test", "Emp Beta Alhut", UserRole.Employee, "QA-Beta", "Alhut", "Employee", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "emp.beta.text@test", "Emp Beta Text", UserRole.Employee, "QA-Beta", "Text", "Employee", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "emp.beta.br@test", "Emp Beta BR", UserRole.Employee, "QA-Beta", "BR", "Employee", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "emp.beta.hakam@test", "Emp Beta Hakam", UserRole.Employee, "QA-Beta", "Hakam", "Employee", companies, templates, alhut, br, text, hakam);

        // --- Employees (Gefen companies) ---
        AddUser(defs, "emp.hamasa.alhut@test", "Emp Hamasa Alhut", UserRole.Employee, "Hamasa", "Alhut", "Employee", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "emp.hamasa.br@test", "Emp Hamasa BR", UserRole.Employee, "Hamasa", "BR", "Employee", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "emp.kabah.text@test", "Emp Kabah Text", UserRole.Employee, "Kabah", "Text", "Employee", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "emp.matot.alhut@test", "Emp Matot Alhut", UserRole.Employee, "Matot", "Alhut", "Employee", companies, templates, alhut, br, text, hakam);

        // --- Employees (Tech/Department) ---
        AddDeptUser(defs, "emp.tech.pie@test", "Emp Tech Pie", UserRole.Employee, "Employee", "Pie", departments, templates);
        AddDeptUser(defs, "emp.tech.tao@test", "Emp Tech Tao", UserRole.Employee, "Employee", "Tao", departments, templates);

        // --- Trainees (Tzafona - all 4 job types) ---
        AddUser(defs, "trainee.alhut@test", "Trainee Alhut", UserRole.Trainee, "Tzafona", "Alhut", "Trainee", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "trainee.text@test", "Trainee Text", UserRole.Trainee, "Tzafona", "Text", "Trainee", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "trainee.br@test", "Trainee BR", UserRole.Trainee, "Tzafona", "BR", "Trainee", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "trainee.hakam@test", "Trainee Hakam", UserRole.Trainee, "Tzafona", "Hakam", "Trainee", companies, templates, alhut, br, text, hakam);

        // --- Assigners ---
        AddUser(defs, "assigner.oren@test", "Assigner Oren", UserRole.Assigner, "Tzafona", null, "Assigner", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "assigner.ella@test", "Assigner Ella", UserRole.Assigner, "Hitazmut", null, "Assigner", companies, templates, alhut, br, text, hakam);

        // --- Special users ---
        AddUser(defs, "nogrants@test", "No Grants", UserRole.Employee, "Tzafona", "Alhut", null, companies, templates, alhut, br, text, hakam);
        AddUser(defs, "locked@test", "Locked User", UserRole.Employee, "Tzafona", "Alhut", "Employee", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "deactivated@test", "Deactivated User", UserRole.Employee, "Tzafona", "Alhut", "Employee", companies, templates, alhut, br, text, hakam);
        AddUser(defs, "concurrent1@test", "Concurrent User", UserRole.Employee, "Tzafona", "Alhut", "Employee", companies, templates, alhut, br, text, hakam);

        return defs;
    }

    private static void AddUser(
        List<TestUserDef> defs, string email, string displayName, UserRole role,
        string companyName, string? jobTypeName, string? templateKey,
        Dictionary<string, Company> companies,
        Dictionary<string, RoleTemplate> templates,
        JobType? alhut, JobType? brJt, JobType? text, JobType? hakam)
    {
        if (!companies.TryGetValue(companyName, out var company))
            return; // Company doesn't exist, skip

        int? jobTypeId = jobTypeName switch
        {
            "Alhut" => alhut?.Id,
            "BR" => brJt?.Id,
            "Text" => text?.Id,
            "Hakam" => hakam?.Id,
            _ => null
        };

        int? roleTemplateId = null;
        if (templateKey != null && templates.TryGetValue(templateKey, out var tmpl))
            roleTemplateId = tmpl.Id;

        defs.Add(new TestUserDef
        {
            Email = email,
            DisplayName = displayName,
            Role = role,
            CompanyId = company.Id,
            JobTypeId = jobTypeId,
            RoleTemplateId = roleTemplateId,
            DepartmentId = null
        });
    }

    private static void AddDeptUser(
        List<TestUserDef> defs, string email, string displayName, UserRole role,
        string? templateKey, string deptName,
        Dictionary<string, Department> departments,
        Dictionary<string, RoleTemplate> templates)
    {
        if (!departments.TryGetValue(deptName, out var dept))
            return;

        int? roleTemplateId = null;
        if (templateKey != null && templates.TryGetValue(templateKey, out var tmpl))
            roleTemplateId = tmpl.Id;

        // Department users need a company — use HQ company for the molecule
        // For now, use CompanyId = 0 as sentinel; we'll resolve it in EnsureUserAsync
        defs.Add(new TestUserDef
        {
            Email = email,
            DisplayName = displayName,
            Role = role,
            CompanyId = 0, // Will be resolved to HQ company
            JobTypeId = null,
            RoleTemplateId = roleTemplateId,
            DepartmentId = dept.Id,
            MoleculeId = dept.MoleculeId
        });
    }

    // ================================================================
    // Infrastructure
    // ================================================================

    private static async Task<Molecule> EnsureQaMoleculeAsync(AppDbContext db, int areaId, ILogger logger)
    {
        var qa = await db.Molecules.FirstOrDefaultAsync(m => m.Name == "QA");
        if (qa != null) return qa;

        qa = new Molecule
        {
            AreaId = areaId,
            Name = "QA",
            DisplayName = "QA Testing",
            Type = MoleculeType.Workforce
        };
        db.Molecules.Add(qa);
        await db.SaveChangesAsync();
        logger.LogInformation("[QaTestUserSeed] Created QA molecule");
        return qa;
    }

    private static async Task EnsureQaCompaniesAsync(
        AppDbContext db, int moleculeId, Dictionary<string, Company> existing, ILogger logger)
    {
        var qaCompanies = new[]
        {
            ("QA-Alpha", "קיו-אלפא"),
            ("QA-Beta", "קיו-בטא")
        };

        foreach (var (name, displayName) in qaCompanies)
        {
            if (existing.ContainsKey(name)) continue;

            var company = new Company
            {
                Name = name,
                DisplayName = displayName,
                MoleculeId = moleculeId
            };
            db.Companies.Add(company);
            logger.LogInformation("[QaTestUserSeed] Created QA company: {Name}", name);
        }

        await db.SaveChangesAsync();
    }

    private static async Task<string> EnsureUserAsync(AppDbContext db, TestUserDef def, ILogger logger)
    {
        // Resolve department users — find HQ company for their molecule
        if (def.CompanyId == 0 && def.MoleculeId.HasValue)
        {
            var hqCompany = await db.Companies
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.MoleculeId == def.MoleculeId.Value && c.IsHeadquarters);

            if (hqCompany != null)
            {
                def.CompanyId = hqCompany.Id;
            }
            else
            {
                // Fallback: any company in the molecule
                var anyCompany = await db.Companies
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(c => c.MoleculeId == def.MoleculeId.Value);

                if (anyCompany != null)
                    def.CompanyId = anyCompany.Id;
                else
                {
                    logger.LogWarning("[QaTestUserSeed] No company found for molecule {MolId}, skipping {Email}",
                        def.MoleculeId, def.Email);
                    return "skipped";
                }
            }
        }

        var existing = await db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == def.Email);

        if (existing != null)
        {
            // Update password and key fields to ensure consistency
            var (hash, salt) = PasswordHasher.CreateHash(TEST_PASSWORD);
            existing.PasswordHash = hash;
            existing.PasswordSalt = salt;
            existing.IsActive = true;
            existing.Role = def.Role;
            existing.CompanyId = def.CompanyId;
            if (def.JobTypeId.HasValue) existing.JobTypeId = def.JobTypeId;
            if (def.DepartmentId.HasValue) existing.DepartmentId = def.DepartmentId;
            if (def.RoleTemplateId.HasValue) existing.RoleTemplateId = def.RoleTemplateId;
            await db.SaveChangesAsync();
            return "updated";
        }

        var (newHash, newSalt) = PasswordHasher.CreateHash(TEST_PASSWORD);
        var user = new AppUser
        {
            Email = def.Email,
            DisplayName = def.DisplayName,
            Role = def.Role,
            CompanyId = def.CompanyId,
            JobTypeId = def.JobTypeId,
            DepartmentId = def.DepartmentId,
            RoleTemplateId = def.RoleTemplateId,
            IsActive = true,
            HasCompletedOnboarding = true,
            PasswordHash = newHash,
            PasswordSalt = newSalt
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return "created";
    }

    private static async Task HandleSpecialCases(AppDbContext db, Dictionary<string, Company> companies, ILogger logger)
    {
        // locked@test — set lockout
        var locked = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Email == "locked@test");
        if (locked != null)
        {
            locked.FailedLoginAttempts = 5;
            locked.LockoutEnd = DateTime.UtcNow.AddHours(24);
            locked.LastLoginAttempt = DateTime.UtcNow;
        }

        // deactivated@test — deactivate
        var deactivated = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Email == "deactivated@test");
        if (deactivated != null)
        {
            deactivated.IsActive = false;
        }

        // nogrants@test — ensure NO RoleTemplate (no grants)
        var nogrants = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Email == "nogrants@test");
        if (nogrants != null)
        {
            nogrants.RoleTemplateId = null;
        }
    }

    private static bool IsTestEnvironment()
    {
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        return env == "Development" || env == "Test";
    }

    private class TestUserDef
    {
        public string Email { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public UserRole Role { get; set; }
        public int CompanyId { get; set; }
        public int? JobTypeId { get; set; }
        public int? DepartmentId { get; set; }
        public int? RoleTemplateId { get; set; }
        public int? MoleculeId { get; set; }
    }
}
