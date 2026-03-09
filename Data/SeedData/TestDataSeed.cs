using Microsoft.EntityFrameworkCore;
using ShiftManager.Models;
using ShiftManager.Models.Support;

namespace ShiftManager.Data.SeedData;

/// <summary>
/// Test data strategy for E2E testing with Playwright.
/// Seeds consistent test scenarios for reliable UI testing.
///
/// IMPORTANT: This is only seeded in Development/Test environments.
/// Production databases NEVER receive test data.
/// </summary>
// SECURITY-AUDITED: All IgnoreQueryFilters() in this class are SAFE — dev-only test data seeding, not user-facing, guarded by IsTestEnvironment() check
public static class TestDataSeed
{
    #region Test User Constants

    /// <summary>
    /// Test user credentials for E2E testing.
    /// All test users use standardized passwords per role level.
    /// </summary>
    public static class TestUsers
    {
        // Owner - Full system access
        public const string OwnerEmail = "test.owner@shifty.test";
        public const string OwnerPassword = "TestOwner123!";
        public const string OwnerDisplayName = "Test Owner";

        // Director - Multi-company access
        public const string DirectorEmail = "test.director@shifty.test";
        public const string DirectorPassword = "TestDirector123!";
        public const string DirectorDisplayName = "Test Director";

        // Manager - Single company management
        public const string ManagerEmail = "test.manager@shifty.test";
        public const string ManagerPassword = "TestManager123!";
        public const string ManagerDisplayName = "Test Manager";

        // Member - Personal calendar access
        public const string MemberEmail = "test.member@shifty.test";
        public const string MemberPassword = "TestMember123!";
        public const string MemberDisplayName = "Test Member";

        // Assigner - Shift/chore assignment
        public const string AssignerEmail = "test.assigner@shifty.test";
        public const string AssignerPassword = "TestAssigner123!";
        public const string AssignerDisplayName = "Test Assigner";

        // NoGrants - Edge case user with no permissions
        public const string NoGrantsEmail = "test.nogrants@shifty.test";
        public const string NoGrantsPassword = "TestNoGrants123!";
        public const string NoGrantsDisplayName = "Test NoGrants";
    }

    #endregion

    #region Test Company Constants

    /// <summary>
    /// Test company identifiers for different test scenarios.
    /// </summary>
    public static class TestCompanies
    {
        public const string EmptyCalendarCompanyName = "Test Company Empty";
        public const string EmptyCalendarCompanySlug = "test-company-empty";

        public const string FullCalendarCompanyName = "Test Company Full";
        public const string FullCalendarCompanySlug = "test-company-full";

        public const string MultiCompanyAName = "Multi Test A";
        public const string MultiCompanyASlug = "multi-test-a";

        public const string MultiCompanyBName = "Multi Test B";
        public const string MultiCompanyBSlug = "multi-test-b";
    }

    #endregion

    #region Seeding Methods

    /// <summary>
    /// Seeds test-specific data for E2E tests.
    /// Only runs in Development/Test environments.
    /// This method is idempotent - safe to call multiple times.
    /// </summary>
    public static async Task SeedTestDataAsync(AppDbContext context, ILogger logger)
    {
        // Only seed in test environment
        if (!IsTestEnvironment())
        {
            logger.LogInformation("Skipping test data seed - not in Development/Test environment");
            return;
        }

        logger.LogInformation("Starting E2E test data seeding...");

        // Get the Shifty molecule for test companies (or create one if needed)
        var molecule = await GetOrCreateTestMoleculeAsync(context, logger);
        if (molecule == null)
        {
            logger.LogWarning("Could not find or create molecule for test companies");
            return;
        }

        // Create test companies
        var emptyCalendarCompany = await EnsureTestCompanyExistsAsync(
            context, TestCompanies.EmptyCalendarCompanyName,
            TestCompanies.EmptyCalendarCompanySlug, molecule.Id, logger);

        var fullCalendarCompany = await EnsureTestCompanyExistsAsync(
            context, TestCompanies.FullCalendarCompanyName,
            TestCompanies.FullCalendarCompanySlug, molecule.Id, logger);

        var multiCompanyA = await EnsureTestCompanyExistsAsync(
            context, TestCompanies.MultiCompanyAName,
            TestCompanies.MultiCompanyASlug, molecule.Id, logger);

        var multiCompanyB = await EnsureTestCompanyExistsAsync(
            context, TestCompanies.MultiCompanyBName,
            TestCompanies.MultiCompanyBSlug, molecule.Id, logger);

        // Get a default JobType for test users (first job type in the molecule's area)
        int? defaultJobTypeId = null;
        if (molecule != null)
        {
            var jobType = await context.JobTypes
                .FirstOrDefaultAsync(jt => jt.AreaId == molecule.AreaId);
            defaultJobTypeId = jobType?.Id;
        }

        // Create test users in appropriate companies
        await CreateTestUserAsync(context, TestUsers.OwnerEmail, TestUsers.OwnerPassword,
            UserRole.Owner, TestUsers.OwnerDisplayName, fullCalendarCompany.Id, logger, defaultJobTypeId);

        await CreateTestUserAsync(context, TestUsers.DirectorEmail, TestUsers.DirectorPassword,
            UserRole.Director, TestUsers.DirectorDisplayName, multiCompanyA.Id, logger, defaultJobTypeId);

        await CreateTestUserAsync(context, TestUsers.ManagerEmail, TestUsers.ManagerPassword,
            UserRole.Manager, TestUsers.ManagerDisplayName, fullCalendarCompany.Id, logger, defaultJobTypeId);

        await CreateTestUserAsync(context, TestUsers.MemberEmail, TestUsers.MemberPassword,
            UserRole.Employee, TestUsers.MemberDisplayName, fullCalendarCompany.Id, logger, defaultJobTypeId);

        await CreateTestUserAsync(context, TestUsers.AssignerEmail, TestUsers.AssignerPassword,
            UserRole.Assigner, TestUsers.AssignerDisplayName, fullCalendarCompany.Id, logger, defaultJobTypeId);

        await CreateTestUserAsync(context, TestUsers.NoGrantsEmail, TestUsers.NoGrantsPassword,
            UserRole.Trainee, TestUsers.NoGrantsDisplayName, emptyCalendarCompany.Id, logger, defaultJobTypeId);

        // Set up Director with multi-company access
        await SetupDirectorMultiCompanyAccessAsync(context, multiCompanyA.Id, multiCompanyB.Id, logger);

        // Seed calendar data for full calendar company
        await SeedFullCalendarDataAsync(context, fullCalendarCompany.Id, logger);

        logger.LogInformation("E2E test data seeding completed");
    }

    /// <summary>
    /// Resets test data to a known state before test runs.
    /// Useful for ensuring consistent starting state.
    /// </summary>
    public static async Task ResetTestDataAsync(AppDbContext context, ILogger logger)
    {
        if (!IsTestEnvironment())
        {
            logger.LogWarning("Cannot reset test data outside of Development/Test environment");
            return;
        }

        logger.LogInformation("Resetting test data to known state...");

        // Get test companies
        var testCompanySlugs = new[]
        {
            TestCompanies.EmptyCalendarCompanySlug,
            TestCompanies.FullCalendarCompanySlug,
            TestCompanies.MultiCompanyASlug,
            TestCompanies.MultiCompanyBSlug
        };

        var testCompanies = await context.Companies
            .IgnoreQueryFilters()
            .Where(c => testCompanySlugs.Contains(c.Slug))
            .ToListAsync();

        foreach (var company in testCompanies)
        {
            // Clear shift instances for this company (calendar data)
            var shiftInstances = await context.ShiftInstances
                .IgnoreQueryFilters()
                .Where(si => si.CompanyId == company.Id)
                .ToListAsync();

            context.ShiftInstances.RemoveRange(shiftInstances);

            // Clear shift assignments
            var shiftAssignments = await context.ShiftAssignments
                .IgnoreQueryFilters()
                .Where(sa => sa.CompanyId == company.Id)
                .ToListAsync();

            context.ShiftAssignments.RemoveRange(shiftAssignments);
        }

        await context.SaveChangesAsync();

        // Re-seed the test data
        await SeedTestDataAsync(context, logger);

        logger.LogInformation("Test data reset completed");
    }

    #endregion

    #region Private Helper Methods

    private static bool IsTestEnvironment()
    {
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        return env == "Development" || env == "Test";
    }

    private static async Task<Molecule?> GetOrCreateTestMoleculeAsync(AppDbContext context, ILogger logger)
    {
        // Try to find the Oren molecule (first workforce molecule in Shifty)
        var molecule = await context.Molecules
            .FirstOrDefaultAsync(m => m.Name == "Oren");

        if (molecule != null)
        {
            return molecule;
        }

        // If no Oren molecule, try to find any molecule
        molecule = await context.Molecules.FirstOrDefaultAsync();

        if (molecule != null)
        {
            logger.LogInformation("Using molecule {MoleculeName} for test companies", molecule.Name);
            return molecule;
        }

        // If no molecules exist at all, we can't create test companies
        // This means ShiftyOrganizationSeed hasn't run yet
        logger.LogWarning("No molecules found - ShiftyOrganizationSeed may not have run");
        return null;
    }

    private static async Task<Company> EnsureTestCompanyExistsAsync(
        AppDbContext context, string name, string slug, int moleculeId, ILogger logger)
    {
        var company = await context.Companies
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Slug == slug);

        if (company == null)
        {
            company = new Company
            {
                Name = name,
                Slug = slug,
                DisplayName = name,
                NameHe = name,
                MoleculeId = moleculeId
            };
            context.Companies.Add(company);
            await context.SaveChangesAsync();
            logger.LogInformation("Created test company: {CompanySlug}", slug);
        }

        return company;
    }

    private static async Task CreateTestUserAsync(
        AppDbContext context, string email, string password, UserRole role,
        string displayName, int companyId, ILogger logger, int? jobTypeId = null)
    {
        var existingUser = await context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email);

        if (existingUser != null)
        {
            // Update password, job type, and role template in case they changed
            var (hash, salt) = PasswordHasher.CreateHash(password);
            existingUser.PasswordHash = hash;
            existingUser.PasswordSalt = salt;
            existingUser.IsActive = true;
            if (jobTypeId.HasValue && existingUser.JobTypeId == null)
                existingUser.JobTypeId = jobTypeId.Value;
            if (existingUser.RoleTemplateId == null)
                existingUser.RoleTemplateId = await MapRoleToTemplateIdAsync(context, role, jobTypeId);
            await context.SaveChangesAsync();
            logger.LogInformation("Updated test user: {Email}", email);
            return;
        }

        var (newHash, newSalt) = PasswordHasher.CreateHash(password);
        var user = new AppUser
        {
            Email = email,
            DisplayName = displayName,
            CompanyId = companyId,
            Role = role,
            IsActive = true,
            JobTypeId = jobTypeId,
            RoleTemplateId = await MapRoleToTemplateIdAsync(context, role, jobTypeId),
            PasswordHash = newHash,
            PasswordSalt = newSalt
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();
        logger.LogInformation("Created test user: {Email} with role {Role}", email, role);
    }

    private static async Task SetupDirectorMultiCompanyAccessAsync(
        AppDbContext context, int companyAId, int companyBId, ILogger logger)
    {
        var director = await context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == TestUsers.DirectorEmail);

        if (director == null)
        {
            logger.LogWarning("Director user not found for multi-company setup");
            return;
        }

        // Check if director already has access to companies
        var existingMappings = await context.Set<DirectorCompany>()
            .Where(dc => dc.UserId == director.Id && !dc.IsDeleted)
            .ToListAsync();

        if (!existingMappings.Any(dc => dc.CompanyId == companyAId))
        {
            context.Set<DirectorCompany>().Add(new DirectorCompany
            {
                UserId = director.Id,
                CompanyId = companyAId,
                GrantedAt = DateTime.UtcNow,
                GrantedBy = director.Id,
                IsDeleted = false
            });
        }

        if (!existingMappings.Any(dc => dc.CompanyId == companyBId))
        {
            context.Set<DirectorCompany>().Add(new DirectorCompany
            {
                UserId = director.Id,
                CompanyId = companyBId,
                GrantedAt = DateTime.UtcNow,
                GrantedBy = director.Id,
                IsDeleted = false
            });
        }

        await context.SaveChangesAsync();
        logger.LogInformation("Set up director multi-company access for companies {A} and {B}", companyAId, companyBId);
    }

    private static async Task SeedFullCalendarDataAsync(AppDbContext context, int companyId, ILogger logger)
    {
        // Get the company's molecule to properly scope shift types
        var company = await context.Companies
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == companyId);

        if (company == null)
        {
            logger.LogWarning("Company {CompanyId} not found for calendar seeding", companyId);
            return;
        }

        // Get a JobType from the same area as the molecule (needed for Calendar/Shifts page filtering)
        var molecule = await context.Molecules
            .FirstOrDefaultAsync(m => m.Id == company.MoleculeId);

        int? jobTypeId = null;
        if (molecule != null)
        {
            var jobType = await context.JobTypes
                .FirstOrDefaultAsync(jt => jt.AreaId == molecule.AreaId);
            jobTypeId = jobType?.Id;
        }

        // Check if shift types exist for this company
        var shiftTypes = await context.ShiftTypes
            .IgnoreQueryFilters()
            .Where(st => st.CompanyId == companyId)
            .ToListAsync();

        if (!shiftTypes.Any())
        {
            // Create basic shift types with proper molecule/jobType scoping
            // so they appear in the Calendar/Shifts page dropdown filters
            var morningShift = new ShiftType
            {
                CompanyId = companyId,
                MoleculeId = company.MoleculeId,
                JobTypeId = jobTypeId,
                Key = ShiftType.KEY_MORNING,
                CustomName = "Morning Shift",
                Start = new TimeOnly(6, 0),
                End = new TimeOnly(14, 0)
            };

            var afternoonShift = new ShiftType
            {
                CompanyId = companyId,
                MoleculeId = company.MoleculeId,
                JobTypeId = jobTypeId,
                Key = ShiftType.KEY_AFTERNOON,
                CustomName = "Afternoon Shift",
                Start = new TimeOnly(14, 0),
                End = new TimeOnly(22, 0)
            };

            var nightShift = new ShiftType
            {
                CompanyId = companyId,
                MoleculeId = company.MoleculeId,
                JobTypeId = jobTypeId,
                Key = ShiftType.KEY_NIGHT,
                CustomName = "Night Shift",
                Start = new TimeOnly(22, 0),
                End = new TimeOnly(6, 0)
            };

            context.ShiftTypes.AddRange(morningShift, afternoonShift, nightShift);
            await context.SaveChangesAsync();

            shiftTypes = new List<ShiftType> { morningShift, afternoonShift, nightShift };
            logger.LogInformation("Created {Count} shift types for test company", shiftTypes.Count);
        }

        // Check if shift instances already exist
        var today = DateOnly.FromDateTime(DateTime.Today);
        var existingInstances = await context.ShiftInstances
            .IgnoreQueryFilters()
            .Where(si => si.CompanyId == companyId)
            .AnyAsync();

        if (existingInstances)
        {
            logger.LogInformation("Shift instances already exist for test company");
            return;
        }

        // Create 30 days of shifts
        var instances = new List<ShiftInstance>();
        for (int day = -15; day <= 15; day++)
        {
            var workDate = today.AddDays(day);

            foreach (var shiftType in shiftTypes)
            {
                instances.Add(new ShiftInstance
                {
                    CompanyId = companyId,
                    ShiftTypeId = shiftType.Id,
                    WorkDate = workDate,
                    StaffingRequired = 2,
                    Name = $"Test {shiftType.Name}"
                });
            }
        }

        context.ShiftInstances.AddRange(instances);
        await context.SaveChangesAsync();
        logger.LogInformation("Created {Count} shift instances for test company", instances.Count);
    }

    /// <summary>
    /// Maps a UserRole + optional JobTypeId to the correct RoleTemplate ID.
    /// Uses the same logic as the migration backfill SQL.
    /// </summary>
    private static async Task<int?> MapRoleToTemplateIdAsync(AppDbContext context, UserRole role, int? jobTypeId)
    {
        string templateKey = role switch
        {
            UserRole.Owner => "Owner",
            UserRole.Assigner => "Assigner",
            UserRole.AreaAdmin => "AreaAdmin",
            UserRole.Employee => "Employee",
            UserRole.Trainee => "Trainee",
            UserRole.Manager => await GetManagerTemplateKeyAsync(context, jobTypeId),
            UserRole.Director => await GetDirectorTemplateKeyAsync(context, jobTypeId),
            _ => "Employee"
        };

        var template = await context.RoleTemplates.FirstOrDefaultAsync(rt => rt.Key == templateKey);
        return template?.Id;
    }

    private static async Task<string> GetManagerTemplateKeyAsync(AppDbContext context, int? jobTypeId)
    {
        if (jobTypeId == null) return "BRDirector";
        var jobType = await context.JobTypes.FirstOrDefaultAsync(jt => jt.Id == jobTypeId);
        return jobType?.Name switch
        {
            "Alhut" or "Text" => "Lead",
            _ => "BRDirector"
        };
    }

    private static async Task<string> GetDirectorTemplateKeyAsync(AppDbContext context, int? jobTypeId)
    {
        if (jobTypeId == null) return "MoleculeAdmin";
        var jobType = await context.JobTypes.FirstOrDefaultAsync(jt => jt.Id == jobTypeId);
        return jobType?.Name switch
        {
            "Alhut" or "Text" => "Director",
            _ => "MoleculeAdmin"
        };
    }

    #endregion
}
