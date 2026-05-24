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

namespace ShiftManager.Tests.UnitTests.Services;

/// <summary>
/// Tests for Task A6 — per-row 6-month sparkline series.
///
/// Validates that <see cref="IJusticeService.GetSparklineSeriesAsync"/> correctly:
///   • buckets work items by calendar month (oldest → newest)
///   • returns a list of length == buckets for every row in scope
///   • attributes work to the correct row key at UsersInCompany, CompaniesInMolecule levels
///   • returns all-zeros when a row has no work in the span
///
/// Also validates the <see cref="IJusticeService.GetJusticeViewAsync"/> wiring:
///   • includeSparklines=true  → every row.Sparkline has length == 6
///   • includeSparklines=false → every row.Sparkline is null (default)
///
/// Seed strategy: chores for a single user spread over Dec 2025 – May 2026
///   • Dec 2025 : 0   (first bucket of a 6-month span ending May 2026)
///   • Jan 2026 : 0
///   • Feb 2026 : 0
///   • Mar 2026 : 2
///   • Apr 2026 : 5
///   • May 2026 : 3
/// Total = 10 chores across 3 distinct months.
/// </summary>
public class JusticeServiceSparklineComparisonTests : IDisposable
{
    // ---------------------------------------------------------------
    // Hierarchy IDs
    // ---------------------------------------------------------------
    private const int AreaId      = 1;
    private const int MoleculeId  = 1;
    private const int CompanyAId  = 1;  // contains the primary user (U1)
    private const int CompanyBId  = 2;  // contains U2; added for CompaniesInMolecule test

    private const int UserId1     = 101; // in CompanyA — has 2+5+3 chores across 3 months
    private const int UserId2     = 201; // in CompanyB — 0 chores (all-zeros test)

    // Period: 6-month window ending 2026-05-31
    private static readonly DateOnly PeriodEnd   = new DateOnly(2026, 5, 31);
    private static readonly DateOnly PeriodStart = new DateOnly(2025, 12, 1);

    // ---------------------------------------------------------------
    // Infrastructure
    // ---------------------------------------------------------------
    private readonly SqliteConnection _sqliteConnection;
    private readonly AppDbContext _db;
    private readonly JusticeService _service;

    public JusticeServiceSparklineComparisonTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _sqliteConnection.Open();

        var tenantMock = new Mock<ITenantResolver>();
        tenantMock.Setup(t => t.GetCurrentTenantId()).Returns(CompanyAId);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        _db = new AppDbContext(options, tenantMock.Object);
        _db.Database.EnsureCreated();

        var httpAccessor = new Mock<IHttpContextAccessor>();
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "999"),
            new Claim("CompanyId", CompanyAId.ToString())
        }, "TestAuth"));
        httpAccessor.Setup(x => x.HttpContext).Returns(ctx);

        var busyService = BusyServiceMockFactory.Real(_db);
        var choreService = new ChoreService(
            _db, Mock.Of<ITenantResolver>(), httpAccessor.Object, Mock.Of<IDirectorService>(),
            Mock.Of<IGrantService>(), Mock.Of<ILogger<ChoreService>>(), Mock.Of<ICompanyCacheService>(),
            busyService);
        var onDutyService = new OnDutyService(
            _db, httpAccessor.Object, Mock.Of<IDirectorService>(), Mock.Of<IGrantService>(),
            Mock.Of<ILogger<OnDutyService>>(), Mock.Of<IFeatureFlagService>(), busyService);
        var shiftAssignmentService = new Mock<IShiftAssignmentService>(MockBehavior.Strict).Object;

        _service = new JusticeService(_db, shiftAssignmentService, choreService, onDutyService);

        SeedAll();
    }

    // ---------------------------------------------------------------
    // Seed
    // ---------------------------------------------------------------
    private void SeedAll()
    {
        _db.Areas.Add(new Area { Id = AreaId, ProjectId = 0, Name = "Area1", DisplayName = "Area1" });
        _db.Molecules.Add(new Molecule { Id = MoleculeId, AreaId = AreaId, Name = "Mol1", DisplayName = "Mol1" });
        _db.Companies.AddRange(
            new Company { Id = CompanyAId, MoleculeId = MoleculeId, Name = "CoA", DisplayName = "CoA" },
            new Company { Id = CompanyBId, MoleculeId = MoleculeId, Name = "CoB", DisplayName = "CoB" });

        _db.Users.AddRange(
            new AppUser { Id = UserId1, Email = "u1@co.com", DisplayName = "U1", CompanyId = CompanyAId, IsActive = true, Role = UserRole.Employee },
            new AppUser { Id = UserId2, Email = "u2@co.com", DisplayName = "U2", CompanyId = CompanyBId, IsActive = true, Role = UserRole.Employee });

        // Global chore target — needed for JusticeView rows to resolve Expected.
        _db.JusticeTargets.Add(new JusticeTarget
        {
            Id = 1,
            CompanyId = null,
            WorkType = JusticeWorkType.Chore,
            ScopeKind = JusticeScope.Global,
            ScopeId = null,
            ExpectedCount = 1m,
            PeriodKind = PeriodKind.PerMonth,
            UpdatedAt = DateTime.UtcNow
        });

        _db.SaveChanges();

        int choreId = 1;

        // UserId1 chores: 2 in March, 5 in April, 3 in May 2026
        // March 2026
        for (int i = 1; i <= 2; i++)
            _db.Chores.Add(new Chore { Id = choreId++, CompanyId = CompanyAId, UserId = UserId1, Date = new DateOnly(2026, 3, i), CanceledAt = null, Title = "t" });

        // April 2026
        for (int i = 1; i <= 5; i++)
            _db.Chores.Add(new Chore { Id = choreId++, CompanyId = CompanyAId, UserId = UserId1, Date = new DateOnly(2026, 4, i), CanceledAt = null, Title = "t" });

        // May 2026
        for (int i = 1; i <= 3; i++)
            _db.Chores.Add(new Chore { Id = choreId++, CompanyId = CompanyAId, UserId = UserId1, Date = new DateOnly(2026, 5, i), CanceledAt = null, Title = "t" });

        // UserId2 has no chores (all-zeros verification)

        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
    }

    // ---------------------------------------------------------------
    // Query helpers
    // ---------------------------------------------------------------

    // UsersInCompany query scoped to CompanyA, 6-month span ending May 2026
    private static JusticeQuery UsersInCompanyQuery() => new JusticeQuery(
        Scope: JusticeScope.Company,
        ScopeId: CompanyAId,
        PeriodStart: PeriodStart,
        PeriodEnd: PeriodEnd,
        WorkType: JusticeWorkType.Chore,
        ExcludeExemptShifts: false,
        Level: JusticeLevel.UsersInCompany);

    // CompaniesInMolecule query scoped to Molecule1, same time span
    private static JusticeQuery CompaniesInMoleculeQuery() => new JusticeQuery(
        Scope: JusticeScope.Molecule,
        ScopeId: MoleculeId,
        PeriodStart: PeriodStart,
        PeriodEnd: PeriodEnd,
        WorkType: JusticeWorkType.Chore,
        ExcludeExemptShifts: false,
        Level: JusticeLevel.CompaniesInMolecule);

    // ===============================================================
    // A6.1 — UsersInCompany: sparkline buckets length + values
    // ===============================================================

    /// <summary>
    /// The returned dictionary must contain an entry for UserId1.
    /// Its list must have exactly 6 elements (one per calendar month, Dec..May).
    /// The last 3 buckets (Mar, Apr, May) must equal [2, 5, 3]; the first 3 must be 0.
    /// </summary>
    [Fact]
    public async Task GetSparklineSeriesAsync_UsersInCompany_CorrectBucketsAndValues()
    {
        var q = UsersInCompanyQuery();
        var result = await _service.GetSparklineSeriesAsync(q, buckets: 6, CancellationToken.None);

        result.Should().ContainKey(UserId1, "UserId1 has chores in the span");

        var sparkline = result[UserId1];
        sparkline.Should().HaveCount(6, "6 monthly buckets requested");

        // Dec 2025, Jan 2026, Feb 2026 → 0 chores each
        sparkline[0].Should().Be(0m, "Dec 2025: no chores");
        sparkline[1].Should().Be(0m, "Jan 2026: no chores");
        sparkline[2].Should().Be(0m, "Feb 2026: no chores");

        // Mar 2026 → 2 chores
        sparkline[3].Should().Be(2m, "Mar 2026: 2 chores");
        // Apr 2026 → 5 chores
        sparkline[4].Should().Be(5m, "Apr 2026: 5 chores");
        // May 2026 → 3 chores
        sparkline[5].Should().Be(3m, "May 2026: 3 chores");
    }

    /// <summary>
    /// UserId2 is not in CompanyA, so CompanyA-scoped query must NOT include UserId2.
    /// (It is in CompanyB — this confirms scope isolation.)
    /// </summary>
    [Fact]
    public async Task GetSparklineSeriesAsync_UsersInCompany_DoesNotIncludeOtherCompanyUser()
    {
        var q = UsersInCompanyQuery();
        var result = await _service.GetSparklineSeriesAsync(q, buckets: 6, CancellationToken.None);

        result.Should().NotContainKey(UserId2,
            "UserId2 belongs to CompanyB, not in the CompanyA-scoped query");
    }

    // ===============================================================
    // A6.2 — CompaniesInMolecule: per-company monthly sums
    // ===============================================================

    /// <summary>
    /// At CompaniesInMolecule level the row key is companyId.
    /// CompanyA's sparkline must aggregate UserId1's chores (2+5+3).
    /// CompanyB's sparkline must be all zeros (UserId2 has no chores).
    /// </summary>
    [Fact]
    public async Task GetSparklineSeriesAsync_CompaniesInMolecule_PerCompanyMonthlySums()
    {
        var q = CompaniesInMoleculeQuery();
        var result = await _service.GetSparklineSeriesAsync(q, buckets: 6, CancellationToken.None);

        result.Should().ContainKey(CompanyAId, "CompanyA has work in span");
        result.Should().ContainKey(CompanyBId, "CompanyB exists in molecule and must appear (all-zeros)");

        var coASparkline = result[CompanyAId];
        coASparkline.Should().HaveCount(6);
        coASparkline[0].Should().Be(0m);
        coASparkline[1].Should().Be(0m);
        coASparkline[2].Should().Be(0m);
        coASparkline[3].Should().Be(2m, "Mar 2026 aggregated to CompanyA");
        coASparkline[4].Should().Be(5m, "Apr 2026 aggregated to CompanyA");
        coASparkline[5].Should().Be(3m, "May 2026 aggregated to CompanyA");

        var coBSparkline = result[CompanyBId];
        coBSparkline.Should().HaveCount(6);
        coBSparkline.Should().AllBeEquivalentTo(0m, "CompanyB has no chores — all zeros");
    }

    // ===============================================================
    // A6.3 — GetJusticeViewAsync wiring
    // ===============================================================

    /// <summary>
    /// When includeSparklines=true, every row in the returned view must have
    /// a non-null Sparkline of length 6.
    /// </summary>
    [Fact]
    public async Task GetJusticeViewAsync_WithSparklines_PopulatesSparklineOnEachRow()
    {
        var q = UsersInCompanyQuery();
        var vm = await _service.GetJusticeViewAsync(q, drillableChildIds: null, includeSparklines: true, CancellationToken.None);

        vm.Rows.Should().NotBeEmpty();
        foreach (var row in vm.Rows)
        {
            row.Sparkline.Should().NotBeNull($"row {row.Id} must have Sparkline when includeSparklines=true");
            row.Sparkline!.Count.Should().Be(6, $"row {row.Id} Sparkline must have 6 buckets");
        }
    }

    /// <summary>
    /// When includeSparklines=false (the default), every row must have Sparkline == null.
    /// This ensures no extra DB queries are issued in the existing fast paths.
    /// </summary>
    [Fact]
    public async Task GetJusticeViewAsync_WithoutSparklines_LeavesSparklineNull()
    {
        var q = UsersInCompanyQuery();
        // Use the overload that does NOT pass includeSparklines — defaults to false.
        var vm = await _service.GetJusticeViewAsync(q, CancellationToken.None);

        vm.Rows.Should().NotBeEmpty();
        foreach (var row in vm.Rows)
        {
            row.Sparkline.Should().BeNull($"row {row.Id}: Sparkline must be null when includeSparklines=false");
        }
    }

    /// <summary>
    /// The row for UserId1 returned by GetJusticeViewAsync with sparklines must carry
    /// the correct per-bucket values (same as the raw GetSparklineSeriesAsync contract).
    /// </summary>
    [Fact]
    public async Task GetJusticeViewAsync_WithSparklines_RowSparklineValuesCorrect()
    {
        var q = UsersInCompanyQuery();
        var vm = await _service.GetJusticeViewAsync(q, drillableChildIds: null, includeSparklines: true, CancellationToken.None);

        var user1Row = vm.Rows.Single(r => r.Id == UserId1);
        user1Row.Sparkline.Should().NotBeNull();
        user1Row.Sparkline![3].Should().Be(2m, "Mar 2026 bucket");
        user1Row.Sparkline![4].Should().Be(5m, "Apr 2026 bucket");
        user1Row.Sparkline![5].Should().Be(3m, "May 2026 bucket");
    }

    // ===============================================================
    // A7 — A/B period comparison with per-row deltas
    // ===============================================================

    // Query helpers for May 2026 (primary) and April 2026 (compare) at CompaniesInMolecule level.
    // The seed has: CompanyA → UserId1 has 3 chores in May, 5 chores in April.
    // CompanyA.Actual(May)=3, CompanyA.Actual(April)=5 → delta = 3 - 5 = -2.

    private static JusticeQuery CompaniesInMoleculeMayQuery() => new JusticeQuery(
        Scope: JusticeScope.Molecule,
        ScopeId: MoleculeId,
        PeriodStart: new DateOnly(2026, 5, 1),
        PeriodEnd: new DateOnly(2026, 5, 31),
        WorkType: JusticeWorkType.Chore,
        ExcludeExemptShifts: false,
        Level: JusticeLevel.CompaniesInMolecule);

    private static JusticeQuery CompaniesInMoleculeAprilQuery() => new JusticeQuery(
        Scope: JusticeScope.Molecule,
        ScopeId: MoleculeId,
        PeriodStart: new DateOnly(2026, 4, 1),
        PeriodEnd: new DateOnly(2026, 4, 30),
        WorkType: JusticeWorkType.Chore,
        ExcludeExemptShifts: false,
        Level: JusticeLevel.CompaniesInMolecule);

    // UsersInCompany for May 2026 (10 chores in May 2026 per task spec):
    // The seed already has UserId1 with 3 chores in May. We need a separate test
    // that seeds 10 in May and 6 in April. We do this in the UsersInCompany scope.

    private static JusticeQuery UsersInCompanyMayQuery() => new JusticeQuery(
        Scope: JusticeScope.Company,
        ScopeId: CompanyAId,
        PeriodStart: new DateOnly(2026, 5, 1),
        PeriodEnd: new DateOnly(2026, 5, 31),
        WorkType: JusticeWorkType.Chore,
        ExcludeExemptShifts: false,
        Level: JusticeLevel.CompaniesInMolecule);

    private static JusticeQuery UsersInCompanyAprilQuery() => new JusticeQuery(
        Scope: JusticeScope.Company,
        ScopeId: CompanyAId,
        PeriodStart: new DateOnly(2026, 4, 1),
        PeriodEnd: new DateOnly(2026, 4, 30),
        WorkType: JusticeWorkType.Chore,
        ExcludeExemptShifts: false,
        Level: JusticeLevel.CompaniesInMolecule);

    /// <summary>
    /// A7.1 — Basic delta: CompaniesInMolecule with May as primary, April as compare.
    /// CompanyA had 3 chores in May and 5 in April → ActualDeltaByRowId[CompanyAId] == -2.
    /// The Primary row for CompanyA must have DeltaVsCompare == -2.
    /// CompanyB had 0 in both periods → delta == 0.
    /// </summary>
    [Fact]
    public async Task GetComparisonViewAsync_CompaniesInMolecule_DeltaIsCorrect()
    {
        var mayQ = CompaniesInMoleculeMayQuery();
        var aprilQ = CompaniesInMoleculeAprilQuery();

        var result = await _service.GetComparisonViewAsync(mayQ, aprilQ, drillableChildIds: null, CancellationToken.None);

        // Delta dict
        result.ActualDeltaByRowId.Should().ContainKey(CompanyAId);
        result.ActualDeltaByRowId[CompanyAId].Should().Be(-2m,
            "CompanyA: May(3) - April(5) = -2");

        result.ActualDeltaByRowId.Should().ContainKey(CompanyBId);
        result.ActualDeltaByRowId[CompanyBId].Should().Be(0m,
            "CompanyB had 0 in both periods → delta 0");

        // Primary row DeltaVsCompare propagated
        var primaryRowA = result.Primary.Rows.Single(r => r.Id == CompanyAId);
        primaryRowA.DeltaVsCompare.Should().Be(-2m,
            "Primary row DeltaVsCompare must equal ActualDeltaByRowId value");

        var primaryRowB = result.Primary.Rows.Single(r => r.Id == CompanyBId);
        primaryRowB.DeltaVsCompare.Should().Be(0m,
            "Primary row B DeltaVsCompare == 0 when both periods are 0");
    }

    /// <summary>
    /// A7.2 — CompanyA has 3 chores in May and 5 in April.
    /// Using May as primary with 10-chore scenario: seed additional 7 chores in May
    /// so total is 10. Compare to April (5). Delta must be 10 - 6 = 4.
    ///
    /// We use a fresh fixture via a nested helper that adds the extra chores on top of
    /// the base seed (UserId1 already has 3 in May; we add 7 more = 10 total). But since
    /// the seed is shared, we instead verify the task-spec example directly:
    /// The task says "10 in May, 6 in April". We treat CompanyA-scoped at UsersInCompany level
    /// but use CompaniesInMolecule scope to get CompanyA as a row.
    ///
    /// For this test class we use the pre-existing seed: May=3, April=5, delta=-2.
    /// The spec example (10 vs 6) is verified in the separate A7.3 test below.
    /// </summary>
    [Fact]
    public async Task GetComparisonViewAsync_PrimaryRowsRetainOriginalSortAndFields()
    {
        var mayQ = CompaniesInMoleculeMayQuery();
        var aprilQ = CompaniesInMoleculeAprilQuery();

        var result = await _service.GetComparisonViewAsync(mayQ, aprilQ, drillableChildIds: null, CancellationToken.None);

        // Primary must carry the May query
        result.Primary.Query.Should().Be(mayQ);
        // Compare must carry the April query
        result.Compare.Query.Should().Be(aprilQ);

        // Primary rows must be non-empty and all have DeltaVsCompare set (not null)
        result.Primary.Rows.Should().NotBeEmpty();
        foreach (var row in result.Primary.Rows)
        {
            row.DeltaVsCompare.Should().NotBeNull(
                $"row {row.Id}: DeltaVsCompare must be populated for all primary rows");
        }

        // Compare rows must NOT have DeltaVsCompare set (returned as-is)
        foreach (var row in result.Compare.Rows)
        {
            row.DeltaVsCompare.Should().BeNull(
                $"compare row {row.Id}: DeltaVsCompare must remain null on the compare view");
        }
    }

    /// <summary>
    /// A7.3 — Task-spec example: Company has 10 chores in May 2026 and 6 in April 2026.
    /// Seed the additional chores required to reach those totals, then assert delta == 4.
    ///
    /// This uses a separate SQLite in-memory fixture to avoid polluting the shared seed.
    /// </summary>
    [Fact]
    public async Task GetComparisonViewAsync_TaskSpecExample_DeltaIsFour()
    {
        // ---- Build a separate, isolated fixture ----
        using var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();

        var tenantMock = new Mock<ITenantResolver>();
        tenantMock.Setup(t => t.GetCurrentTenantId()).Returns(CompanyAId);

        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn)
            .Options;
        using var db = new AppDbContext(opts, tenantMock.Object);
        db.Database.EnsureCreated();

        // Minimal hierarchy
        db.Areas.Add(new Area { Id = AreaId, ProjectId = 0, Name = "Area1", DisplayName = "Area1" });
        db.Molecules.Add(new Molecule { Id = MoleculeId, AreaId = AreaId, Name = "Mol1", DisplayName = "Mol1" });
        db.Companies.Add(new Company { Id = CompanyAId, MoleculeId = MoleculeId, Name = "CoA", DisplayName = "CoA" });
        db.Users.Add(new AppUser { Id = UserId1, Email = "u1@co.com", DisplayName = "U1", CompanyId = CompanyAId, IsActive = true, Role = UserRole.Employee });
        db.JusticeTargets.Add(new JusticeTarget
        {
            Id = 1,
            CompanyId = null,
            WorkType = JusticeWorkType.Chore,
            ScopeKind = JusticeScope.Global,
            ScopeId = null,
            ExpectedCount = 1m,
            PeriodKind = PeriodKind.PerMonth,
            UpdatedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        // 10 chores in May 2026
        int cid2 = 1;
        for (int i = 1; i <= 10; i++)
            db.Chores.Add(new Chore { Id = cid2++, CompanyId = CompanyAId, UserId = UserId1, Date = new DateOnly(2026, 5, i), CanceledAt = null, Title = "t" });

        // 6 chores in April 2026
        for (int i = 1; i <= 6; i++)
            db.Chores.Add(new Chore { Id = cid2++, CompanyId = CompanyAId, UserId = UserId1, Date = new DateOnly(2026, 4, i), CanceledAt = null, Title = "t" });

        db.SaveChanges();

        var httpAccessor = new Mock<IHttpContextAccessor>();
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "999"),
            new Claim("CompanyId", CompanyAId.ToString())
        }, "TestAuth"));
        httpAccessor.Setup(x => x.HttpContext).Returns(ctx);

        var busyService = BusyServiceMockFactory.Real(db);
        var choreService = new ChoreService(
            db, Mock.Of<ITenantResolver>(), httpAccessor.Object, Mock.Of<IDirectorService>(),
            Mock.Of<IGrantService>(), Mock.Of<ILogger<ChoreService>>(), Mock.Of<ICompanyCacheService>(),
            busyService);
        var onDutyService = new OnDutyService(
            db, httpAccessor.Object, Mock.Of<IDirectorService>(), Mock.Of<IGrantService>(),
            Mock.Of<ILogger<OnDutyService>>(), Mock.Of<IFeatureFlagService>(), busyService);
        var sas = new Mock<IShiftAssignmentService>(MockBehavior.Strict).Object;
        var svc = new JusticeService(db, sas, choreService, onDutyService);

        var mayQ = new JusticeQuery(
            Scope: JusticeScope.Company,
            ScopeId: CompanyAId,
            PeriodStart: new DateOnly(2026, 5, 1),
            PeriodEnd: new DateOnly(2026, 5, 31),
            WorkType: JusticeWorkType.Chore,
            ExcludeExemptShifts: false,
            Level: JusticeLevel.UsersInCompany);

        var aprilQ = new JusticeQuery(
            Scope: JusticeScope.Company,
            ScopeId: CompanyAId,
            PeriodStart: new DateOnly(2026, 4, 1),
            PeriodEnd: new DateOnly(2026, 4, 30),
            WorkType: JusticeWorkType.Chore,
            ExcludeExemptShifts: false,
            Level: JusticeLevel.UsersInCompany);

        var result = await svc.GetComparisonViewAsync(mayQ, aprilQ, drillableChildIds: null, CancellationToken.None);

        // Task spec: CompanyA (via UserId1 row) has 10 in May, 6 in April → delta == 4
        result.ActualDeltaByRowId.Should().ContainKey(UserId1);
        result.ActualDeltaByRowId[UserId1].Should().Be(4m,
            "UserId1: May(10) - April(6) = 4 (task spec example)");

        var primaryRow = result.Primary.Rows.Single(r => r.Id == UserId1);
        primaryRow.DeltaVsCompare.Should().Be(4m,
            "Primary row DeltaVsCompare for UserId1 must equal 4");
    }

    /// <summary>
    /// A7.4 — Row present only in primary (not in compare): delta must equal the primary actual.
    /// Scenario: UserId2 has chores only in May (not in April).
    /// </summary>
    [Fact]
    public async Task GetComparisonViewAsync_RowOnlyInPrimary_DeltaEqualsPrimaryActual()
    {
        // Isolated fixture: CompanyA has UserId1 (both months) and UserId2 (May only)
        using var conn = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        conn.Open();

        var tenantMock = new Mock<ITenantResolver>();
        tenantMock.Setup(t => t.GetCurrentTenantId()).Returns(CompanyAId);

        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn)
            .Options;
        using var db = new AppDbContext(opts, tenantMock.Object);
        db.Database.EnsureCreated();

        db.Areas.Add(new Area { Id = AreaId, ProjectId = 0, Name = "Area1", DisplayName = "Area1" });
        db.Molecules.Add(new Molecule { Id = MoleculeId, AreaId = AreaId, Name = "Mol1", DisplayName = "Mol1" });
        db.Companies.Add(new Company { Id = CompanyAId, MoleculeId = MoleculeId, Name = "CoA", DisplayName = "CoA" });

        // UserId1: present in both periods
        // UserId2: active in May (will appear in primary rows) but has no chores in April
        db.Users.AddRange(
            new AppUser { Id = UserId1, Email = "u1@co.com", DisplayName = "U1", CompanyId = CompanyAId, IsActive = true, Role = UserRole.Employee },
            new AppUser { Id = UserId2, Email = "u2@co.com", DisplayName = "U2", CompanyId = CompanyAId, IsActive = true, Role = UserRole.Employee });
        db.JusticeTargets.Add(new JusticeTarget
        {
            Id = 1,
            CompanyId = null,
            WorkType = JusticeWorkType.Chore,
            ScopeKind = JusticeScope.Global,
            ScopeId = null,
            ExpectedCount = 1m,
            PeriodKind = PeriodKind.PerMonth,
            UpdatedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        int cid3 = 1;
        // UserId1: 3 chores in May, 2 chores in April
        for (int i = 1; i <= 3; i++)
            db.Chores.Add(new Chore { Id = cid3++, CompanyId = CompanyAId, UserId = UserId1, Date = new DateOnly(2026, 5, i), CanceledAt = null, Title = "t" });
        for (int i = 1; i <= 2; i++)
            db.Chores.Add(new Chore { Id = cid3++, CompanyId = CompanyAId, UserId = UserId1, Date = new DateOnly(2026, 4, i), CanceledAt = null, Title = "t" });

        // UserId2: 5 chores in May only, zero in April
        for (int i = 1; i <= 5; i++)
            db.Chores.Add(new Chore { Id = cid3++, CompanyId = CompanyAId, UserId = UserId2, Date = new DateOnly(2026, 5, i), CanceledAt = null, Title = "t" });

        db.SaveChanges();

        var httpAccessor = new Mock<IHttpContextAccessor>();
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "999"),
            new Claim("CompanyId", CompanyAId.ToString())
        }, "TestAuth"));
        httpAccessor.Setup(x => x.HttpContext).Returns(ctx);

        var busyService = BusyServiceMockFactory.Real(db);
        var choreService = new ChoreService(
            db, Mock.Of<ITenantResolver>(), httpAccessor.Object, Mock.Of<IDirectorService>(),
            Mock.Of<IGrantService>(), Mock.Of<ILogger<ChoreService>>(), Mock.Of<ICompanyCacheService>(),
            busyService);
        var onDutyService = new OnDutyService(
            db, httpAccessor.Object, Mock.Of<IDirectorService>(), Mock.Of<IGrantService>(),
            Mock.Of<ILogger<OnDutyService>>(), Mock.Of<IFeatureFlagService>(), busyService);
        var sas = new Mock<IShiftAssignmentService>(MockBehavior.Strict).Object;
        var svc = new JusticeService(db, sas, choreService, onDutyService);

        var mayQ = new JusticeQuery(
            Scope: JusticeScope.Company,
            ScopeId: CompanyAId,
            PeriodStart: new DateOnly(2026, 5, 1),
            PeriodEnd: new DateOnly(2026, 5, 31),
            WorkType: JusticeWorkType.Chore,
            ExcludeExemptShifts: false,
            Level: JusticeLevel.UsersInCompany);

        var aprilQ = new JusticeQuery(
            Scope: JusticeScope.Company,
            ScopeId: CompanyAId,
            PeriodStart: new DateOnly(2026, 4, 1),
            PeriodEnd: new DateOnly(2026, 4, 30),
            WorkType: JusticeWorkType.Chore,
            ExcludeExemptShifts: false,
            Level: JusticeLevel.UsersInCompany);

        var result = await svc.GetComparisonViewAsync(mayQ, aprilQ, drillableChildIds: null, CancellationToken.None);

        // UserId2 has 5 in May, 0 in April → delta = 5 - 0 = 5
        result.ActualDeltaByRowId.Should().ContainKey(UserId2);
        result.ActualDeltaByRowId[UserId2].Should().Be(5m,
            "UserId2 only appears in primary (May=5, April=0) → delta = 5");

        var primaryRow2 = result.Primary.Rows.Single(r => r.Id == UserId2);
        primaryRow2.DeltaVsCompare.Should().Be(5m,
            "Primary row for UserId2 must have DeltaVsCompare = 5");

        // UserId1 delta = 3 - 2 = 1
        result.ActualDeltaByRowId[UserId1].Should().Be(1m,
            "UserId1: May(3) - April(2) = 1");
    }
}
