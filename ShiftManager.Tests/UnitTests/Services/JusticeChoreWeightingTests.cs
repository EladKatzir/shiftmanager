using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using ShiftManager.Data;
using ShiftManager.Models;
using ShiftManager.Models.Support;
using ShiftManager.Services;
using ShiftManager.Tests.Helpers;
using System.Security.Claims;
using Xunit;

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Phase 5 (Duration-weighted Fairness). Verifies the chore branches of JusticeService:
///   • Task 2: chore Actual = Σ WeightMinutes (NOT count); optional ChoreCategoryId narrows it.
///   • Task 3: chore sparkline buckets sum WeightMinutes; category filter applies.
///   • Task 4: chore Expected target is scaled by DEFAULT_CHORE_WEIGHT_MINUTES (480) so the
///     chore-equivalent target compares against weighted minutes.
///
/// Mirrors the real-SQLite harness of JusticeServiceCategoryTests.
///
/// Seed (UsersInCompany, MoleculeId=1 / CompanyId=1, fully-past period so endCap == PeriodEnd):
///   ChoreCategoryPhysical (10) → ChoreTypePhysical (20)
///   ChoreCategoryComputer (11) → ChoreTypeComputer (21)
///   U1 (201): chore Physical 480min + chore Computer 480min  → count 2, minutes 960
///   U2 (202): chore Physical 240min + chore Physical 240min  → count 2, minutes 480
/// </summary>
public class JusticeChoreWeightingTests : IDisposable
{
    private const int AreaId = 1;
    private const int MoleculeId = 1;
    private const int CompanyId = 1;

    private const int CatPhysical = 10;
    private const int CatComputer = 11;
    private const int TypePhysical = 20;
    private const int TypeComputer = 21;

    private const int U1 = 201;
    private const int U2 = 202;

    private static readonly DateOnly PeriodStart = new(2026, 3, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 3, 31);

    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly JusticeService _service;

    public JusticeChoreWeightingTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _conn.Open();

        var tenantMock = new Mock<ITenantResolver>();
        tenantMock.Setup(t => t.GetCurrentTenantId()).Returns(CompanyId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_conn)
            .Options;

        _db = new AppDbContext(options, tenantMock.Object);
        _db.Database.EnsureCreated();

        var httpAccessor = new Mock<IHttpContextAccessor>();
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "999"),
            new Claim("CompanyId", CompanyId.ToString())
        }, "TestAuth"));
        httpAccessor.Setup(x => x.HttpContext).Returns(ctx);

        var busyService = BusyServiceMockFactory.Real(_db);
        var choreService = new ChoreService(
            _db, Mock.Of<ITenantResolver>(), httpAccessor.Object, Mock.Of<IDirectorService>(),
            Mock.Of<IGrantService>(), Mock.Of<ILogger<ChoreService>>(), Mock.Of<ICompanyCacheService>(),
            busyService, new EligibilityEvaluator());
        var onDutyService = new OnDutyService(
            _db, httpAccessor.Object, Mock.Of<IDirectorService>(), Mock.Of<IGrantService>(),
            Mock.Of<ILogger<OnDutyService>>(), Mock.Of<IFeatureFlagService>(), busyService);
        var shiftAssignmentService = new Mock<IShiftAssignmentService>(MockBehavior.Strict).Object;

        _service = new JusticeService(_db, shiftAssignmentService, choreService, onDutyService);

        SeedAll();
    }

    private void SeedAll()
    {
        _db.Areas.Add(new Area { Id = AreaId, ProjectId = 0, Name = "Area1", DisplayName = "Area1" });
        _db.Molecules.Add(new Molecule { Id = MoleculeId, AreaId = AreaId, Name = "Mol1", DisplayName = "Mol1" });
        _db.Companies.Add(new Company { Id = CompanyId, MoleculeId = MoleculeId, Name = "Co1", DisplayName = "Co1" });

        // Chore categories (Phase 1 entity).
        _db.ChoreCategories.AddRange(
            new ChoreCategory { Id = CatPhysical, MoleculeId = MoleculeId, Name = "Physical", DisplayName = "Physical", SortOrder = 1, IsActive = true },
            new ChoreCategory { Id = CatComputer, MoleculeId = MoleculeId, Name = "Computer", DisplayName = "Computer", SortOrder = 2, IsActive = true });

        // Chore types, each linked to a category (Phase 1 ChoreType.ChoreCategoryId).
        _db.ChoreTypes.AddRange(
            new ChoreType { Id = TypePhysical, MoleculeId = MoleculeId, Name = "Phys", DisplayName = "Phys", ChoreCategoryId = CatPhysical, IsActive = true },
            new ChoreType { Id = TypeComputer, MoleculeId = MoleculeId, Name = "Comp", DisplayName = "Comp", ChoreCategoryId = CatComputer, IsActive = true });

        _db.Users.AddRange(
            new AppUser { Id = U1, Email = "u1@test.com", DisplayName = "UserOne", CompanyId = CompanyId, IsActive = true, Role = UserRole.Employee, AccountType = AccountType.Standard },
            new AppUser { Id = U2, Email = "u2@test.com", DisplayName = "UserTwo", CompanyId = CompanyId, IsActive = true, Role = UserRole.Employee, AccountType = AccountType.Standard });

        _db.SaveChanges();

        // Chores: equal COUNT (2 each), unequal total WeightMinutes (U1=960, U2=480).
        _db.Chores.AddRange(
            new Chore { Id = 1, CompanyId = CompanyId, MoleculeId = MoleculeId, UserId = U1, ChoreTypeId = TypePhysical, Date = new DateOnly(2026, 3, 10), Title = "p", WeightMinutes = 480, CreatedAt = DateTime.UtcNow },
            new Chore { Id = 2, CompanyId = CompanyId, MoleculeId = MoleculeId, UserId = U1, ChoreTypeId = TypeComputer, Date = new DateOnly(2026, 3, 11), Title = "c", WeightMinutes = 480, CreatedAt = DateTime.UtcNow },
            new Chore { Id = 3, CompanyId = CompanyId, MoleculeId = MoleculeId, UserId = U2, ChoreTypeId = TypePhysical, Date = new DateOnly(2026, 3, 12), Title = "p", WeightMinutes = 240, CreatedAt = DateTime.UtcNow },
            new Chore { Id = 4, CompanyId = CompanyId, MoleculeId = MoleculeId, UserId = U2, ChoreTypeId = TypePhysical, Date = new DateOnly(2026, 3, 13), Title = "p", WeightMinutes = 240, CreatedAt = DateTime.UtcNow });

        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    private static JusticeQuery MakeChoreQuery(int? choreCategoryId = null, DateOnly? start = null, DateOnly? end = null) => new JusticeQuery(
        Scope: JusticeScope.Company,
        ScopeId: CompanyId,
        PeriodStart: start ?? PeriodStart,
        PeriodEnd: end ?? PeriodEnd,
        WorkType: JusticeWorkType.Chore,
        ExcludeExemptShifts: false,
        Level: JusticeLevel.UsersInCompany,
        ChoreCategoryId: choreCategoryId);

    // ---- Task 1 query-shape (kept here so the file is the single source of truth) ----

    [Fact]
    public void JusticeQuery_ChoreCategoryId_DefaultsToNull()
    {
        MakeChoreQuery().ChoreCategoryId.Should().BeNull();
    }

    [Fact]
    public void JusticeQuery_ChoreCategoryId_RoundTripsViaWith()
    {
        var q = MakeChoreQuery();
        (q with { ChoreCategoryId = 42 }).ChoreCategoryId.Should().Be(42);
        q.ChoreCategoryId.Should().BeNull();
    }

    // ---- Task 2: weighted actual ----

    [Fact]
    public async Task ChoreActual_IsSumOfWeightMinutes_NotCount()
    {
        var vm = await _service.GetJusticeViewAsync(MakeChoreQuery(), CancellationToken.None);

        var u1 = vm.Rows.Single(r => r.Id == U1);
        var u2 = vm.Rows.Single(r => r.Id == U2);

        u1.Actual.Should().Be(960m, "U1 has two 480-minute chores = 960 weighted minutes (NOT a count of 2)");
        u2.Actual.Should().Be(480m, "U2 has two 240-minute chores = 480 weighted minutes (NOT a count of 2)");
        u1.Actual.Should().NotBe(u2.Actual, "equal chore counts but unequal durations must produce unequal weighted Actual");
    }

    [Fact]
    public async Task ChoreActual_CategoryFilter_NarrowsToMatchingType()
    {
        // Filter to Physical: U1 keeps only its 1 Physical chore (480), U2 keeps both Physical chores (480).
        var vm = await _service.GetJusticeViewAsync(MakeChoreQuery(choreCategoryId: CatPhysical), CancellationToken.None);

        var u1 = vm.Rows.Single(r => r.Id == U1);
        var u2 = vm.Rows.Single(r => r.Id == U2);

        u1.Actual.Should().Be(480m, "filtering to Physical drops U1's Computer chore, leaving one 480-minute Physical chore");
        u2.Actual.Should().Be(480m, "both of U2's chores are Physical (240+240)");
    }

    [Fact]
    public async Task ChoreActual_CategoryFilter_Computer_OnlyU1HasComputerLoad()
    {
        var vm = await _service.GetJusticeViewAsync(MakeChoreQuery(choreCategoryId: CatComputer), CancellationToken.None);

        var u1 = vm.Rows.Single(r => r.Id == U1);
        var u2 = vm.Rows.Single(r => r.Id == U2);

        u1.Actual.Should().Be(480m, "U1 has one Computer chore worth 480 minutes");
        u2.Actual.Should().Be(0m, "U2 has no Computer chores");
    }

    // ---- Task 3: sparkline minutes ----

    [Fact]
    public async Task ChoreSparkline_BucketsSumWeightMinutes_NotCount()
    {
        // 1 bucket ending in the PeriodEnd month (2026-03). All seeded chores fall in March.
        var series = await _service.GetSparklineSeriesAsync(MakeChoreQuery(), buckets: 1, CancellationToken.None);

        series.Should().ContainKey(U1);
        series.Should().ContainKey(U2);
        series[U1].Last().Should().Be(960m, "U1's single bucket sums two 480-minute chores (NOT a count of 2)");
        series[U2].Last().Should().Be(480m, "U2's single bucket sums two 240-minute chores (NOT a count of 2)");
    }

    [Fact]
    public async Task ChoreSparkline_CategoryFilter_NarrowsBucketMinutes()
    {
        var series = await _service.GetSparklineSeriesAsync(MakeChoreQuery(choreCategoryId: CatComputer), buckets: 1, CancellationToken.None);

        series[U1].Last().Should().Be(480m, "only U1's single Computer chore (480) counts under the Computer filter");
        series[U2].Last().Should().Be(0m, "U2 has no Computer chores");
    }

    // ---- Task 4: chore target scaling (×480) ----

    [Fact]
    public async Task ChoreExpected_IsScaledBy480_SoUnitsMatchWeightedActual()
    {
        // Global per-user chore target: 1 chore-equivalent per month.
        _db.JusticeTargets.Add(new JusticeTarget
        {
            Id = 500,
            WorkType = JusticeWorkType.Chore,
            ScopeKind = JusticeScope.Global,
            ScopeId = null,
            ExpectedCount = 1m,
            PeriodKind = PeriodKind.PerMonth
        });
        _db.SaveChanges();

        var vm = await _service.GetJusticeViewAsync(MakeChoreQuery(), CancellationToken.None);

        // PeriodMultiplier = inclusive-days / 30.4375. The seed period is 2026-03-01..2026-03-31 = 31 days.
        const decimal daysPerMonth = 30.4375m;
        var inclusiveDays = (decimal)(PeriodEnd.DayNumber - PeriodStart.DayNumber + 1); // 31
        var multiplier = inclusiveDays / daysPerMonth;
        var expectedMinutes = 1m * multiplier * 480m;

        var u1 = vm.Rows.Single(r => r.Id == U1);
        u1.Expected.Should().BeApproximately(expectedMinutes, 0.001m,
            "chore Expected = target(1) × periodMultiplier × DEFAULT_CHORE_WEIGHT_MINUTES(480), matching weighted-minute Actual units");
    }
}
