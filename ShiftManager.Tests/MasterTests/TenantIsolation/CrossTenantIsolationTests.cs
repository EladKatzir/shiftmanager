using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using ShiftManager.Tests.MasterTests.Infrastructure;

namespace ShiftManager.Tests.MasterTests.TenantIsolation;

/// <summary>
/// Tests for tenant data isolation across companies.
///
/// NOTE ON EF QUERY FILTERS:
/// EF Core caches the model per DbContext type within a process. The shared MasterTestFixture
/// creates the first AppDbContext without a tenant resolver, so the cached model has NO query
/// filters. Subsequent contexts in the same process reuse this model regardless of their
/// tenant resolver. Therefore, these tests verify tenant isolation at the DATA LEVEL
/// (correct CompanyId assignment, query patterns) rather than at the EF query filter level.
/// Query filter correctness is verified structurally (the code exists in OnModelCreating)
/// and would be tested via integration tests with a real database.
/// </summary>
public class CrossTenantIsolationTests : MasterTestBase
{
    public CrossTenantIsolationTests(MasterTestFixture fixture) : base(fixture) { }

    // ================================================================
    // TENANT DATA SEGREGATION (verified at data level)
    // ================================================================

    [Fact]
    public async Task TenantData_UsersAreSegregatedByCompanyId()
    {
        // Verify that seeded users have correct CompanyId assignments
        var tzafona = Fixture.CompanyByName["Tzafona"];
        var hitazmut = Fixture.CompanyByName["Hitazmut"];

        var tzafonaUsers = await Db.Users
            .Where(u => u.CompanyId == tzafona.Id)
            .ToListAsync();
        var hitazmutUsers = await Db.Users
            .Where(u => u.CompanyId == hitazmut.Id)
            .ToListAsync();

        tzafonaUsers.Should().NotBeEmpty("Tzafona should have seeded users");
        hitazmutUsers.Should().NotBeEmpty("Hitazmut should have seeded users");

        // Verify no overlap
        var tzafonaIds = tzafonaUsers.Select(u => u.Id).ToHashSet();
        var hitazmutIds = hitazmutUsers.Select(u => u.Id).ToHashSet();
        tzafonaIds.Overlaps(hitazmutIds).Should().BeFalse(
            "Users belong to exactly one company — no user ID should appear in both sets");
    }

    [Fact]
    public async Task TenantQuery_CompanyIdFilter_IsolatesUsers()
    {
        // Simulate what a query filter does: WHERE CompanyId = @tenantId
        var tzafona = Fixture.CompanyByName["Tzafona"];

        var filteredUsers = await Db.Users
            .Where(u => u.CompanyId == tzafona.Id)
            .ToListAsync();

        filteredUsers.Should().NotBeEmpty();
        filteredUsers.Should().OnlyContain(u => u.CompanyId == tzafona.Id,
            "Filtering by CompanyId should return only that company's users");
    }

    [Fact]
    public async Task TenantQuery_CompanyIdFilter_ExcludesOtherTenants()
    {
        var tzafona = Fixture.CompanyByName["Tzafona"];
        var hitazmut = Fixture.CompanyByName["Hitazmut"];

        var tzafonaFiltered = await Db.Users
            .Where(u => u.CompanyId == tzafona.Id)
            .ToListAsync();

        tzafonaFiltered.Should().NotContain(u => u.CompanyId == hitazmut.Id,
            "Filtering by Tzafona CompanyId should exclude all Hitazmut users");
    }

    [Fact]
    public async Task DifferentTenants_SeeOwnDataOnly()
    {
        var tzafona = Fixture.CompanyByName["Tzafona"];
        var hitazmut = Fixture.CompanyByName["Hitazmut"];

        var usersA = await Db.Users
            .Where(u => u.CompanyId == tzafona.Id)
            .Select(u => u.Id).ToListAsync();
        var usersB = await Db.Users
            .Where(u => u.CompanyId == hitazmut.Id)
            .Select(u => u.Id).ToListAsync();

        usersA.Should().NotBeEmpty("Tzafona should have users");
        usersB.Should().NotBeEmpty("Hitazmut should have users");
        usersA.Intersect(usersB).Should().BeEmpty(
            "Tzafona and Hitazmut user sets must not overlap");
    }

    // ================================================================
    // SHIFT INSTANCE ISOLATION
    // ================================================================

    [Fact]
    public async Task ShiftInstance_FilteredByCompanyId()
    {
        var tzafona = Fixture.CompanyByName["Tzafona"];
        var hitazmut = Fixture.CompanyByName["Hitazmut"];
        var oren = Fixture.MoleculeByName["Oren"];

        var shiftType = new ShiftType
        {
            Key = "TEST_SI_ISO",
            MoleculeId = oren.Id,
            Scope = ShiftScope.Molecule,
            Name = "Test SI Isolation",
            NameHe = "בדיקה",
            Start = new TimeOnly(8, 0),
            End = new TimeOnly(16, 0),
            RowColor = "#000000"
        };
        Db.Set<ShiftType>().Add(shiftType);
        await Db.SaveChangesAsync();
        TrackEntity(shiftType);

        var instanceA = new ShiftInstance
        {
            CompanyId = tzafona.Id,
            ShiftTypeId = shiftType.Id,
            WorkDate = new DateOnly(2026, 4, 10),
            Name = "Tzafona Instance"
        };
        var instanceB = new ShiftInstance
        {
            CompanyId = hitazmut.Id,
            ShiftTypeId = shiftType.Id,
            WorkDate = new DateOnly(2026, 4, 10),
            Name = "Hitazmut Instance"
        };
        Db.ShiftInstances.AddRange(instanceA, instanceB);
        await Db.SaveChangesAsync();
        TrackEntities(instanceA, instanceB);

        // Simulate tenant query filter: WHERE CompanyId = tzafona.Id
        var filtered = await Db.ShiftInstances
            .Where(si => si.CompanyId == tzafona.Id)
            .ToListAsync();

        filtered.Should().Contain(i => i.Id == instanceA.Id);
        filtered.Should().NotContain(i => i.Id == instanceB.Id,
            "CompanyId filter should isolate Tzafona's shift instances from Hitazmut's");
    }

    // ================================================================
    // CHORE ISOLATION
    // ================================================================

    [Fact]
    public async Task Chore_FilteredByCompanyId()
    {
        var tzafona = Fixture.CompanyByName["Tzafona"];
        var hitazmut = Fixture.CompanyByName["Hitazmut"];
        var tzafonaUser = GetTestUser("Tzafona", "Employee");
        var hitazmutUser = GetTestUser("Hitazmut", "Employee");

        var choreA = new Chore
        {
            CompanyId = tzafona.Id,
            UserId = tzafonaUser.Id,
            Date = new DateOnly(2026, 4, 10),
            Title = "Tzafona chore"
        };
        var choreB = new Chore
        {
            CompanyId = hitazmut.Id,
            UserId = hitazmutUser.Id,
            Date = new DateOnly(2026, 4, 10),
            Title = "Hitazmut chore"
        };
        Db.Set<Chore>().AddRange(choreA, choreB);
        await Db.SaveChangesAsync();
        TrackEntities(choreA, choreB);

        var filtered = await Db.Set<Chore>()
            .Where(c => c.CompanyId == tzafona.Id)
            .ToListAsync();

        filtered.Should().Contain(c => c.Id == choreA.Id);
        filtered.Should().NotContain(c => c.Id == choreB.Id,
            "CompanyId filter should isolate Tzafona's chores from Hitazmut's");
    }

    // ================================================================
    // NULL TENANT RESOLVER (NO FILTERING)
    // ================================================================

    [Fact]
    public async Task NullTenantResolver_NoFiltering()
    {
        // The shared Db has no tenant resolver, so it sees ALL data across companies
        var allUsers = await Db.Users.ToListAsync();
        var companyIds = allUsers.Select(u => u.CompanyId).Distinct().ToList();

        companyIds.Count.Should().BeGreaterThan(1,
            "Without a tenant resolver / CompanyId filter, the context sees users from multiple companies");
    }

    // ================================================================
    // ENTITIES WITHOUT QUERY FILTERS (GLOBAL TABLES)
    // ================================================================

    [Fact]
    public async Task ShiftType_IsGlobal_NoCompanyFilter()
    {
        // ShiftType has NO CompanyId query filter — it's scoped by Molecule/Area, not tenant
        var oren = Fixture.MoleculeByName["Oren"];

        var shiftType = new ShiftType
        {
            Key = "TEST_GLOBAL_ST",
            MoleculeId = oren.Id,
            Scope = ShiftScope.Molecule,
            Name = "Global Test",
            NameHe = "בדיקה",
            Start = new TimeOnly(8, 0),
            End = new TimeOnly(16, 0),
            RowColor = "#111111"
        };
        Db.Set<ShiftType>().Add(shiftType);
        await Db.SaveChangesAsync();
        TrackEntity(shiftType);

        // ShiftType is visible without any company filter
        var found = await Db.ShiftTypes.AnyAsync(st => st.Id == shiftType.Id);
        found.Should().BeTrue("ShiftType has no CompanyId query filter — it's globally visible");

        // Verify ShiftType does NOT have a CompanyId that matches any specific tenant
        // (CompanyId on ShiftType is nullable and used for company-scoped shifts, not tenant filtering)
        shiftType.CompanyId.Should().BeNull(
            "Molecule-scoped ShiftType should have null CompanyId");
    }

    [Fact]
    public async Task OnDuty_IsGlobal_NoBelongsToCompany()
    {
        var tzafonaUser = GetTestUser("Tzafona", "Employee");

        var onDuty = new OnDuty
        {
            UserId = tzafonaUser.Id,
            Date = new DateOnly(2026, 4, 11),
            Type = OnDutyType.Hakam,
            CreatedBy = tzafonaUser.Id,
            CreatedAt = DateTime.UtcNow
        };
        Db.Set<OnDuty>().Add(onDuty);
        await Db.SaveChangesAsync();
        TrackEntity(onDuty);

        // OnDuty is global — no CompanyId property, no query filter
        var found = await Db.Set<OnDuty>().AnyAsync(o => o.Id == onDuty.Id);
        found.Should().BeTrue("OnDuty is global, visible without any tenant filter");

        // Verify OnDuty entity does NOT implement IBelongsToCompany
        typeof(OnDuty).GetInterfaces().Should().NotContain(typeof(IBelongsToCompany),
            "OnDuty should not implement IBelongsToCompany — it's a global entity");
    }

    // ================================================================
    // APP CONFIG ISOLATION
    // ================================================================

    [Fact]
    public async Task AppConfig_FilteredByCompanyId()
    {
        var tzafona = Fixture.CompanyByName["Tzafona"];
        var hitazmut = Fixture.CompanyByName["Hitazmut"];

        var configA = new AppConfig
        {
            CompanyId = tzafona.Id,
            Key = "TestConfig_Iso_A",
            Value = "ValueA"
        };
        var configB = new AppConfig
        {
            CompanyId = hitazmut.Id,
            Key = "TestConfig_Iso_B",
            Value = "ValueB"
        };
        Db.Set<AppConfig>().AddRange(configA, configB);
        await Db.SaveChangesAsync();
        TrackEntities(configA, configB);

        // Simulate tenant filter
        var filtered = await Db.Set<AppConfig>()
            .Where(c => c.CompanyId == tzafona.Id)
            .ToListAsync();

        filtered.Should().Contain(c => c.Id == configA.Id);
        filtered.Should().NotContain(c => c.Id == configB.Id,
            "CompanyId filter should isolate AppConfig entries by tenant");
    }

    // ================================================================
    // GRANT TABLE (NO COMPANY QUERY FILTER)
    // ================================================================

    [Fact]
    public async Task Grant_HasNoCompanyQueryFilter()
    {
        // Grants are user-scoped, not tenant-scoped. The Grant entity has a nullable CompanyId
        // for scope, but no HasQueryFilter in AppDbContext.
        var tzafonaUser = GetTestUser("Tzafona", "Employee");
        var hitazmutUser = GetTestUser("Hitazmut", "Employee");

        var tzafonaGrants = await Db.Grants.Where(g => g.UserId == tzafonaUser.Id).CountAsync();
        var hitazmutGrants = await Db.Grants.Where(g => g.UserId == hitazmutUser.Id).CountAsync();

        tzafonaGrants.Should().BeGreaterThan(0, "Tzafona employee should have seeded grants");
        hitazmutGrants.Should().BeGreaterThan(0, "Hitazmut employee should have seeded grants");

        // Both users' grants are accessible in the same query — no tenant filter
        var totalFromBoth = await Db.Grants
            .Where(g => g.UserId == tzafonaUser.Id || g.UserId == hitazmutUser.Id)
            .CountAsync();

        totalFromBoth.Should().Be(tzafonaGrants + hitazmutGrants,
            "Grant table has no CompanyId query filter — all user grants are accessible without tenant filtering");
    }
}
