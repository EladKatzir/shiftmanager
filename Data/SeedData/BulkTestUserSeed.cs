using Microsoft.EntityFrameworkCore;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;

namespace ShiftManager.Data.SeedData;

/// <summary>
/// Bulk test-population seeder: fills every real company with a full soldier roster so all
/// features (shifts, chores, on-call, drafts, multi-company, Mil/GroupUser capabilities)
/// have realistic data to play with. Only runs in Development/Test environments.
///
/// Per workforce company: 10 Alhut + 5 Text + 3 BR + 2 Hakam soldiers, 1 Lead-Alhut,
/// 1 Lead-Text, 1 Kabar (BRDirector) and 1 trainee. Shikma tech companies swap Alhut/Text
/// for ProjectManager (פרויקטור) / Techno. One MoleculeAdmin per molecule. A handful of
/// Mil (מילואים) and GroupUser accounts spread across multiple companies via
/// CompanyMembership rows (capped at Kabar level — never MoleculeAdmin).
///
/// Idempotent — existing users (matched by email) are SKIPPED, not reset, so anything you
/// change on them while playing survives an app restart. All emails end in "@test" so the
/// Program.cs startup sweep provisions role-template grants via RepairUserGrantsAsync.
/// Deterministic — no Random/DateTime.Now in the data path; re-runs build the same list.
/// </summary>
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — dev-only test data seeding, guarded by IsTestEnvironment() check
public static class BulkTestUserSeed
{
    private const string TEST_PASSWORD = "Test1234!";

    // Companies that get the full workforce roster (Alhut/Text/BR/Hakam)
    private static readonly string[] WorkforceCompanies =
    {
        "Tzafona", "Hir", "Camps", "City", "Radio",          // Oren
        "Hitazmut", "GAP", "Yeadim",                          // Ella
        "Element",                                            // Harava
        "Inside", "Out",                                      // Shaked
        "Hamasa", "Kabah", "Matot",                           // Gefen
        "QA-Alpha", "QA-Beta"                                 // QA
    };

    // Shikma tech companies (ProjectManager/Techno instead of Alhut/Text)
    private static readonly string[] TechCompanies =
    {
        "Yekev", "Snir", "Arbel", "Pie", "Samapkamia", "Tao"
    };

    // Molecules still missing a molecule admin (Oren/Ella already have moladmin.*@test from QaTestUserSeed)
    private static readonly (string Molecule, string Company)[] MoleculeAdmins =
    {
        ("Harava", "Element"),
        ("Shaked", "Inside"),
        ("Gefen", "Hamasa"),
        ("Shikma", "Pie"),
        ("QA", "QA-Alpha")
    };

    // ── Deterministic Hebrew name pools ──
    private static readonly string[] MaleFirst =
    {
        "יובל", "איתי", "נדב", "עומר", "אורי", "דניאל", "תומר", "עידו",
        "אלון", "רועי", "גיא", "ניר", "אסף", "ליאור", "מתן", "שחר",
        "עמית", "יונתן", "אביב", "דור", "נועם", "אריאל", "בן", "אלעד"
    };

    private static readonly string[] FemaleFirst =
    {
        "נועה", "מאיה", "תמר", "שירה", "יעל", "רוני", "ליה", "אביגיל",
        "הילה", "מיכל", "ענבר", "טליה", "אגם", "עדן", "ליהי", "אור",
        "גפן", "שני", "קרן", "דנה", "מור", "הדר", "סתיו", "רותם"
    };

    private static readonly string[] LastNames =
    {
        "כהן", "לוי", "מזרחי", "פרץ", "ביטון", "דהן", "אברהם", "פרידמן",
        "מלכה", "אזולאי", "כץ", "שפירא", "גבאי", "בן דוד", "אדרי", "לוין",
        "טל", "ברק", "שלום", "אוחיון", "חדד", "גולן", "אשכנזי", "בר",
        "רוזן", "שמעוני", "סבן", "עמר", "אלבז", "ניסים"
    };

    private static readonly MilitaryRank[] SoldierRanks =
    {
        MilitaryRank.Turai, MilitaryRank.TuraiRishon, MilitaryRank.RavTurai,
        MilitaryRank.Samal, MilitaryRank.SamalRishon, MilitaryRank.RavSamal
    };

    public static async Task SeedAsync(AppDbContext db, IGrantService grantService, ILogger logger)
    {
        if (!IsTestEnvironment())
        {
            logger.LogInformation("[BulkTestUserSeed] Skipping — not in Development/Test environment");
            return;
        }

        // ============================================================
        // 1. Resolve hierarchy + lookup data
        // ============================================================
        var jobTypes = await db.JobTypes.ToListAsync();
        var alhut = jobTypes.FirstOrDefault(j => j.Name == "Alhut");
        var br = jobTypes.FirstOrDefault(j => j.Name == "BR");
        var text = jobTypes.FirstOrDefault(j => j.Name == "Text");
        var hakam = jobTypes.FirstOrDefault(j => j.Name == "Hakam" && j.MoleculeId == null);
        var projectManager = jobTypes.FirstOrDefault(j => j.Name == "ProjectManager");
        var techno = jobTypes.FirstOrDefault(j => j.Name == "Techno");

        if (alhut == null || br == null || text == null || hakam == null || projectManager == null || techno == null)
        {
            logger.LogWarning("[BulkTestUserSeed] Missing job types — expected Alhut, BR, Text, Hakam, ProjectManager, Techno");
            return;
        }

        var templates = await db.RoleTemplates.ToListAsync();
        var templateMap = templates.ToDictionary(rt => rt.Key, rt => rt);
        if (!templateMap.ContainsKey("Employee") || !templateMap.ContainsKey("Lead") ||
            !templateMap.ContainsKey("BRDirector") || !templateMap.ContainsKey("MoleculeAdmin") ||
            !templateMap.ContainsKey("Trainee"))
        {
            logger.LogWarning("[BulkTestUserSeed] Missing role templates — RoleTemplateSeed may not have run");
            return;
        }

        var molecules = await db.Molecules.ToListAsync();
        var moleculeMap = molecules
            .GroupBy(m => m.Name)
            .ToDictionary(g => g.Key, g => g.First());

        // First-wins for duplicate names (HQ companies share the name "HQ" across molecules —
        // none of our targets are named HQ, so this is safe)
        var allCompanies = await db.Companies.IgnoreQueryFilters().ToListAsync();
        var companyMap = new Dictionary<string, Company>();
        foreach (var c in allCompanies)
        {
            companyMap.TryAdd(c.Name, c);
        }

        // ============================================================
        // 2. QA molecule shift types (gap: molecule "QA" was created by
        //    QaTestUserSeed without the standard workforce shift types)
        // ============================================================
        var shiftTypesCreated = await EnsureQaMoleculeShiftTypesAsync(db, moleculeMap, alhut, text, logger);

        // ============================================================
        // 3. Build the full deterministic definition list
        // ============================================================
        var defs = BuildDefinitions(companyMap, templateMap,
            alhut, br, text, hakam, projectManager, techno, logger);

        // ============================================================
        // 4. Create missing users (existing emails are skipped, not reset,
        //    so manual edits made while playing survive restarts)
        // ============================================================
        var defEmails = defs.Select(d => d.Email).ToList();
        var existingUsers = await db.Users
            .IgnoreQueryFilters()
            .Where(u => defEmails.Contains(u.Email))
            .ToDictionaryAsync(u => u.Email, u => u);

        int created = 0, skipped = 0;
        var (hash, salt) = PasswordHasher.CreateHash(TEST_PASSWORD); // one hash for all — dev-only fixture accounts

        foreach (var def in defs)
        {
            if (existingUsers.ContainsKey(def.Email))
            {
                skipped++;
                continue;
            }

            var user = new AppUser
            {
                Email = def.Email,
                DisplayName = def.DisplayName,
                Role = def.Role,
                CompanyId = def.CompanyId,
                JobTypeId = def.JobTypeId,
                RoleTemplateId = def.RoleTemplateId,
                Rank = def.Rank,
                Gender = def.Gender,
                AccountType = def.AccountType,
                DoesShifts = def.DoesShifts,
                DoesChores = def.DoesChores,
                HireDate = def.HireDate,
                DateOfBirth = def.DateOfBirth,
                IsActive = true,
                HasCompletedOnboarding = true,
                PasswordHash = hash,
                PasswordSalt = salt
            };
            db.Users.Add(user);
            existingUsers[def.Email] = user;
            created++;
        }

        await db.SaveChangesAsync();

        // ============================================================
        // 5. Multi-company memberships for Mil/GroupUser accounts
        //    (primary row mirrors the home company; secondaries get
        //    Epic-5-style per-company role-template grants)
        // ============================================================
        var membershipsCreated = await EnsureMembershipsAsync(db, grantService, templates,
            defs.Where(d => d.ExtraCompanyIds.Count > 0).ToList(), existingUsers, logger);

        logger.LogInformation(
            "[BulkTestUserSeed] Done — users created: {Created}, already present: {Skipped}, memberships created: {Memberships}, QA shift types created: {ShiftTypes}",
            created, skipped, membershipsCreated, shiftTypesCreated);
    }

    // ================================================================
    // Definition building
    // ================================================================

    private static List<UserDef> BuildDefinitions(
        Dictionary<string, Company> companies,
        Dictionary<string, RoleTemplate> templates,
        JobType alhut, JobType br, JobType text, JobType hakam,
        JobType projectManager, JobType techno,
        ILogger logger)
    {
        var defs = new List<UserDef>();
        int g = 0; // global running index — drives deterministic names/ranks/genders

        var employee = templates["Employee"];
        var lead = templates["Lead"];
        var kabar = templates["BRDirector"];
        var molAdmin = templates["MoleculeAdmin"];
        var trainee = templates["Trainee"];

        // --- Full roster per company ---
        var allRosterCompanies = WorkforceCompanies.Select(n => (Name: n, Tech: false))
            .Concat(TechCompanies.Select(n => (Name: n, Tech: true)))
            .ToList();

        int companyIndex = 0;
        foreach (var (name, isTech) in allRosterCompanies)
        {
            if (!companies.TryGetValue(name, out var company))
            {
                logger.LogWarning("[BulkTestUserSeed] Company {Name} not found — skipping its roster", name);
                continue;
            }

            var slug = name.ToLowerInvariant();
            var primaryJob = isTech ? projectManager : alhut;   // 10 soldiers
            var secondaryJob = isTech ? techno : text;          // 5 soldiers
            var primaryCode = isTech ? "pm" : "alhut";
            var secondaryCode = isTech ? "techno" : "text";

            // Soldiers: 10 primary + 5 secondary + 3 BR + 2 Hakam (Employee template)
            var soldierGroups = new (JobType Job, int Count, string Code)[]
            {
                (primaryJob, 10, primaryCode),
                (secondaryJob, 5, secondaryCode),
                (br, 3, "br"),
                (hakam, 2, "hakam")
            };

            foreach (var (job, count, code) in soldierGroups)
            {
                for (int i = 1; i <= count; i++)
                {
                    defs.Add(MakeDef(g, $"{code}{i}.{slug}@test", company.Id, employee, job.Id,
                        rank: SoldierRanks[g % SoldierRanks.Length],
                        doesShifts: true, doesChores: true));
                    g++;
                }
            }

            // Leads: one per soldier job line (מפ"צ)
            defs.Add(MakeDef(g, $"lead.{primaryCode}.{slug}@test", company.Id, lead, primaryJob.Id,
                rank: g % 2 == 0 ? MilitaryRank.RavSamal : MilitaryRank.SegenMishne,
                doesShifts: true, doesChores: true));
            g++;
            defs.Add(MakeDef(g, $"lead.{secondaryCode}.{slug}@test", company.Id, lead, secondaryJob.Id,
                rank: g % 2 == 0 ? MilitaryRank.RavSamal : MilitaryRank.SegenMishne,
                doesShifts: true, doesChores: true));
            g++;

            // Kabar (קב"ר, BRDirector template) — officer rank
            defs.Add(MakeDef(g, $"kabar.{slug}@test", company.Id, kabar, br.Id,
                rank: companyIndex % 2 == 0 ? MilitaryRank.Segen : MilitaryRank.Seren,
                doesShifts: false, doesChores: false));
            g++;

            // One trainee (נחפף) per company, job type alternating between the two soldier lines
            defs.Add(MakeDef(g, $"trainee1.{slug}@test", company.Id, trainee,
                companyIndex % 2 == 0 ? primaryJob.Id : secondaryJob.Id,
                rank: MilitaryRank.Turai,
                doesShifts: false, doesChores: false));
            g++;

            companyIndex++;
        }

        // --- Molecule admins (מפק"מ) for molecules still missing one ---
        foreach (var (moleculeName, companyName) in MoleculeAdmins)
        {
            if (!companies.TryGetValue(companyName, out var company))
                continue;

            defs.Add(MakeDef(g, $"moladmin.{moleculeName.ToLowerInvariant()}@test", company.Id, molAdmin,
                jobTypeId: null, rank: MilitaryRank.Seren,
                doesShifts: false, doesChores: false));
            g++;
        }

        // --- Mil (מילואים) accounts — multi-company, capped at Kabar (never MoleculeAdmin) ---
        var milDefs = new (string Home, string[] Extras, RoleTemplate Template, JobType Job, MilitaryRank Rank)[]
        {
            ("Tzafona", new[] { "Hir" }, employee, alhut, MilitaryRank.Samal),
            ("Hitazmut", new[] { "GAP" }, employee, text, MilitaryRank.SamalRishon),
            ("Element", new[] { "Inside" }, employee, br, MilitaryRank.RavSamal),      // cross-molecule (Harava→Shaked)
            ("Hamasa", new[] { "Kabah", "Matot" }, employee, alhut, MilitaryRank.Samal),
            ("City", new[] { "Radio" }, lead, alhut, MilitaryRank.RavSamalMitkadam),
            ("Yekev", new[] { "Tao" }, kabar, br, MilitaryRank.Seren)
        };

        int milIndex = 1;
        foreach (var (home, extras, template, job, rank) in milDefs)
        {
            if (!companies.TryGetValue(home, out var homeCompany))
            {
                milIndex++;
                continue;
            }

            var def = MakeDef(g, $"mil{milIndex}@test", homeCompany.Id, template, job.Id,
                rank: rank, doesShifts: true, doesChores: false);
            def.DisplayName += " (מיל')";
            def.AccountType = AccountType.Mil;
            def.ExtraCompanyIds = extras
                .Where(companies.ContainsKey)
                .Select(e => companies[e].Id)
                .ToList();
            defs.Add(def);
            g++;
            milIndex++;
        }

        // --- GroupUser (יוזר קיבוצי) accounts — shared administrative logins ---
        var groupDefs = new (string Home, string[] Extras, RoleTemplate Template, string Name)[]
        {
            ("Tzafona", new[] { "Hitazmut" }, employee, "יומנאי צפונה"),               // cross-molecule (Oren→Ella)
            ("Hir", Array.Empty<string>(), lead, "עמדת שו\"ב חיר"),
            ("Pie", new[] { "Snir" }, kabar, "תורן פאי"),
            ("Inside", Array.Empty<string>(), employee, "מוקד פנים")
        };

        int groupIndex = 1;
        foreach (var (home, extras, template, displayName) in groupDefs)
        {
            if (!companies.TryGetValue(home, out var homeCompany))
            {
                groupIndex++;
                continue;
            }

            var def = MakeDef(g, $"group{groupIndex}@test", homeCompany.Id, template,
                jobTypeId: null, rank: MilitaryRank.Turai,
                doesShifts: false, doesChores: false);
            def.DisplayName = displayName;
            def.Gender = Gender.Unspecified;
            def.AccountType = AccountType.GroupUser;
            def.ExtraCompanyIds = extras
                .Where(companies.ContainsKey)
                .Select(e => companies[e].Id)
                .ToList();
            defs.Add(def);
            g++;
            groupIndex++;
        }

        return defs;
    }

    /// <summary>Builds one deterministic user definition from the global running index.</summary>
    private static UserDef MakeDef(int g, string email, int companyId, RoleTemplate template,
        int? jobTypeId, MilitaryRank rank, bool doesShifts, bool doesChores)
    {
        var gender = g % 2 == 0 ? Gender.Male : Gender.Female;
        var firstPool = gender == Gender.Male ? MaleFirst : FemaleFirst;
        var first = firstPool[(g / 2) % firstPool.Length];
        var last = LastNames[g % LastNames.Length];

        return new UserDef
        {
            Email = email,
            DisplayName = $"{first} {last}",
            CompanyId = companyId,
            JobTypeId = jobTypeId,
            RoleTemplateId = template.Id,
            Role = template.DerivedUserRole ?? UserRole.Employee,
            Rank = rank,
            Gender = gender,
            DoesShifts = doesShifts,
            DoesChores = doesChores,
            // Deterministic profile flavor so profile pages aren't empty
            HireDate = new DateOnly(2023, 1, 2).AddDays((g * 11) % 1000),
            DateOfBirth = new DateOnly(1998, 1, 1).AddDays((g * 53) % 2800)
        };
    }

    // ================================================================
    // Multi-company memberships (Mil/GroupUser)
    // ================================================================

    private static async Task<int> EnsureMembershipsAsync(
        AppDbContext db,
        IGrantService grantService,
        List<RoleTemplate> templates,
        List<UserDef> multiCompanyDefs,
        Dictionary<string, AppUser> usersByEmail,
        ILogger logger)
    {
        if (multiCompanyDefs.Count == 0)
            return 0;

        var templateById = templates.ToDictionary(t => t.Id, t => t);
        int createdCount = 0;
        // (userId, templateKey, companyId, jobTypeId) of newly created SECONDARY memberships —
        // these get Epic-5-style per-company grants after the membership rows are saved
        var newSecondaries = new List<(int UserId, string TemplateKey, int CompanyId, int? JobTypeId)>();

        foreach (var def in multiCompanyDefs)
        {
            if (!usersByEmail.TryGetValue(def.Email, out var user))
                continue;

            var memberships = await db.CompanyMemberships
                .IgnoreQueryFilters()
                .Where(m => m.UserId == user.Id)
                .ToListAsync();

            // Primary row mirrors AppUser.CompanyId + its per-company columns (exactly-one-primary invariant)
            var primary = memberships.FirstOrDefault(m => m.CompanyId == user.CompanyId);
            if (primary == null)
            {
                db.CompanyMemberships.Add(new CompanyMembership
                {
                    UserId = user.Id,
                    CompanyId = user.CompanyId,
                    RoleTemplateId = user.RoleTemplateId,
                    JobTypeId = user.JobTypeId,
                    DoesShifts = user.DoesShifts,
                    IsPrimary = true,
                    GrantedBy = 0 // system/backfill sentinel (no FK — see CompanyMembership.GrantedBy remarks)
                });
                createdCount++;
            }
            else if (primary.IsDeleted)
            {
                primary.IsDeleted = false;
                primary.DeletedAt = null;
            }

            foreach (var extraCompanyId in def.ExtraCompanyIds)
            {
                var existing = memberships.FirstOrDefault(m => m.CompanyId == extraCompanyId);
                if (existing != null)
                {
                    if (existing.IsDeleted)
                    {
                        existing.IsDeleted = false;
                        existing.DeletedAt = null;
                    }
                    continue;
                }

                db.CompanyMemberships.Add(new CompanyMembership
                {
                    UserId = user.Id,
                    CompanyId = extraCompanyId,
                    RoleTemplateId = user.RoleTemplateId,
                    JobTypeId = user.JobTypeId,
                    // Mil accounts do shifts in every company they belong to; GroupUsers never do
                    DoesShifts = def.AccountType == AccountType.Mil,
                    IsPrimary = false,
                    GrantedBy = 0
                });
                createdCount++;

                if (user.RoleTemplateId.HasValue && templateById.TryGetValue(user.RoleTemplateId.Value, out var template))
                {
                    newSecondaries.Add((user.Id, template.Key, extraCompanyId, user.JobTypeId));
                }
            }
        }

        await db.SaveChangesAsync();

        // Epic-5 pattern: role-template grants scoped to the MEMBERSHIP company (not the primary),
        // mirroring Pages/Admin/Users.cshtml.Membership.cs OnPostAddMembershipAsync
        foreach (var (userId, templateKey, companyId, jobTypeId) in newSecondaries)
        {
            var scope = await grantService.BuildRoleTemplateScopeAsync(templateKey, companyId, jobTypeId);
            await grantService.AssignRoleTemplateGrantsAsync(userId, templateKey, scope);
        }

        if (newSecondaries.Count > 0)
        {
            logger.LogInformation(
                "[BulkTestUserSeed] Applied per-company grants for {Count} secondary memberships",
                newSecondaries.Count);
        }

        return createdCount;
    }

    // ================================================================
    // QA molecule shift types
    // ================================================================

    private static async Task<int> EnsureQaMoleculeShiftTypesAsync(
        AppDbContext db,
        Dictionary<string, Molecule> molecules,
        JobType alhut, JobType text,
        ILogger logger)
    {
        if (!molecules.TryGetValue("QA", out var qaMolecule))
            return 0; // QA molecule not created yet (QaTestUserSeed hasn't run) — nothing to do

        var existing = await db.ShiftTypes
            .IgnoreQueryFilters()
            .Where(st => st.MoleculeId == qaMolecule.Id)
            .Select(st => new { st.Key, st.JobTypeId })
            .ToListAsync();
        var existingKeys = existing.Select(e => (e.Key, e.JobTypeId)).ToHashSet();

        var candidates = TechShiftTypeSeed.GetWorkforceShiftTypes(qaMolecule.Id, alhut.Id)
            .Concat(TechShiftTypeSeed.GetWorkforceShiftTypes(qaMolecule.Id, text.Id))
            .Concat(TechShiftTypeSeed.GetWorkforceSharedShiftTypes(qaMolecule.Id))
            .Where(st => !existingKeys.Contains((st.Key, st.JobTypeId)))
            .ToList();

        if (candidates.Count == 0)
            return 0;

        db.ShiftTypes.AddRange(candidates);
        await db.SaveChangesAsync();
        logger.LogInformation("[BulkTestUserSeed] Created {Count} shift types for QA molecule", candidates.Count);
        return candidates.Count;
    }

    private static bool IsTestEnvironment()
    {
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        return env == "Development" || env == "Test";
    }

    private sealed class UserDef
    {
        public string Email { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public int CompanyId { get; set; }
        public int? JobTypeId { get; set; }
        public int? RoleTemplateId { get; set; }
        public UserRole Role { get; set; }
        public MilitaryRank Rank { get; set; }
        public Gender Gender { get; set; }
        public AccountType AccountType { get; set; } = AccountType.Standard;
        public bool DoesShifts { get; set; }
        public bool DoesChores { get; set; }
        public DateOnly? HireDate { get; set; }
        public DateOnly? DateOfBirth { get; set; }
        public List<int> ExtraCompanyIds { get; set; } = new();
    }
}
